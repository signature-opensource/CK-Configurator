using CK.Transform.Core;
using CK.TypeScript.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CK.Configurator;

/// <summary>
/// The properties of a JSON(C) document as a tree (the comments of each property kept), and the merge of the layers of a
/// deployment: its view is the central configuration, then the overlays of its bases, then its own (JSON Merge Patch,
/// case insensitive: an object merges, null removes, any other value replaces), each property with its origin.
/// </summary>
sealed class EditorTree
{
    /// <summary>
    /// The origin of a property of the overlay of the viewed deployment.
    /// </summary>
    public const string DeploymentOrigin = "Deployment";

    /// <summary>
    /// The origin of a property that a <c>null</c> of the overlay of the viewed deployment removes.
    /// </summary>
    public const string RemovedOrigin = "Removed";

    /// <summary>
    /// The origin of a property inherited from the central configuration.
    /// </summary>
    public const string CentralOrigin = "Central";

    const StringComparison _comparison = StringComparison.OrdinalIgnoreCase;

    public sealed class Node
    {
        public required string Name { get; init; }
        public required string Kind { get; init; }
        public string? Value { get; init; }
        public string? Comment { get; set; }
        public string? Origin { get; set; }
        public List<Node>? Children { get; init; }

        public Node? Find( string name ) => Children?.FirstOrDefault( c => c.Name.Equals( name, _comparison ) );

        public Node Clone( string? origin )
        {
            return new Node
            {
                Name = Name,
                Kind = Kind,
                Value = Value,
                Comment = Comment,
                Origin = origin,
                Children = Children?.Select( c => c.Clone( origin ) ).ToList()
            };
        }
    }

    public EditorTree( List<Node> root )
    {
        Root = root;
    }

    /// <summary>
    /// Gets the properties of the root object.
    /// </summary>
    public List<Node> Root { get; }

    /// <summary>
    /// Parses a JSON(C) text (it must be valid).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The tree.</returns>
    public static EditorTree Parse( string text )
    {
        var code = new JsonAnalyzer().ParseOrThrow( text );
        var root = code.GetJsonRootObject();
        return new EditorTree( root != null ? Read( code, root ) : [] );
    }

    static List<Node> Read( SourceCode code, JsonObjectSpan o )
    {
        var result = new List<Node>();
        foreach( var p in o.Properties )
        {
            var comment = GetComment( code.Tokens[p.NameIndex] );
            if( p.ObjectValue != null )
            {
                result.Add( new Node { Name = p.Name, Kind = "Object", Comment = comment, Children = Read( code, p.ObjectValue ) } );
                continue;
            }
            var first = code.Tokens[p.ValueSpan.Beg];
            var kind = p.Value is JsonArraySpan
                        ? "Array"
                        : first.TokenType switch
                        {
                            TokenType.GenericString => "String",
                            TokenType.GenericNumber or TokenType.Minus => "Number",
                            _ when first.TextEquals( "null" ) => "Null",
                            _ => "Boolean"
                        };
            result.Add( new Node { Name = p.Name, Kind = kind, Value = code.GetJsonValueText( p ), Comment = comment } );
        }
        return result;
    }

    // The comments on the lines above a property: the comment trivias of its name token, without their markers.
    static string? GetComment( Token name )
    {
        StringBuilder? b = null;
        foreach( var t in name.LeadingTrivias )
        {
            if( !t.IsLineComment && !t.IsBlockComment ) continue;
            var text = t.Content.ToString();
            text = t.IsLineComment
                    ? text.Substring( 2 ).TrimEnd( '\r', '\n' )
                    : text.Substring( 2, text.Length - 4 );
            b ??= new StringBuilder();
            if( b.Length > 0 ) b.Append( '\n' );
            b.Append( text.Trim() );
        }
        return b?.ToString();
    }

    /// <summary>
    /// Finds a property by its path.
    /// </summary>
    /// <param name="path">The path (names separated by ':').</param>
    /// <returns>The property or null.</returns>
    public Node? Find( string path )
    {
        Node? n = null;
        var children = Root;
        foreach( var segment in path.Split( ':' ) )
        {
            n = children?.FirstOrDefault( c => c.Name.Equals( segment, _comparison ) );
            if( n == null ) return null;
            children = n.Children;
        }
        return n;
    }

    /// <summary>
    /// Creates the view of a deployment: the central tree, then the trees of its bases (the nearest last), then its own.
    /// The <c>"$PER_DEPLOYMENT"</c> markers of the central configuration and of the bases, the <c>"$configurator"</c> and
    /// <c>"$schema"</c> of the central configuration and the <c>"$BASE"</c> of the bases are not part of it (the build
    /// removes them before the merge); the overlay of the viewed deployment is shown as it is written.
    /// </summary>
    /// <param name="central">The central tree.</param>
    /// <param name="bases">The bases (their name and tree), from the farthest to the nearest.</param>
    /// <param name="overlay">The overlay of the viewed deployment.</param>
    /// <returns>The merged tree.</returns>
    public static EditorTree Merge( EditorTree central, IEnumerable<(string Name, EditorTree Tree)> bases, EditorTree overlay )
    {
        var root = Strip( central.Root, CentralOrigin, isRoot: true, "$configurator", "$schema" );
        foreach( var (name, tree) in bases )
        {
            Apply( root, Strip( tree.Root, name, isRoot: true, "$BASE" ), isSelf: false );
        }
        Apply( root, overlay.Root.Select( n => n.Clone( DeploymentOrigin ) ).ToList(), isSelf: true );
        return new EditorTree( root );
    }

    // A copy without the "$PER_DEPLOYMENT" markers (at any depth) and the given root keys.
    static List<Node> Strip( List<Node> nodes, string origin, bool isRoot, params string[] rootKeys )
    {
        var result = new List<Node>();
        foreach( var n in nodes )
        {
            if( n.Name.Equals( "$PER_DEPLOYMENT", _comparison ) ) continue;
            if( isRoot && rootKeys.Any( k => k.Equals( n.Name, _comparison ) ) ) continue;
            result.Add( new Node
            {
                Name = n.Name,
                Kind = n.Kind,
                Value = n.Value,
                Comment = n.Comment,
                Origin = origin,
                Children = n.Children != null ? Strip( n.Children, origin, false ) : null
            } );
        }
        return result;
    }

    // The merge patch of a layer (its nodes carry its origin). An overridden property keeps its place.
    static void Apply( List<Node> target, List<Node> patch, bool isSelf )
    {
        foreach( var p in patch )
        {
            int i = target.FindIndex( t => t.Name.Equals( p.Name, _comparison ) );
            var existing = i >= 0 ? target[i] : null;
            if( p.Kind == "Null" )
            {
                if( !isSelf )
                {
                    if( i >= 0 ) target.RemoveAt( i );
                    continue;
                }
                // The viewed deployment removes it: shown, struck through (an inherited comment helps).
                var removed = new Node { Name = existing?.Name ?? p.Name, Kind = "Null", Value = "null", Comment = p.Comment ?? existing?.Comment, Origin = RemovedOrigin };
                if( i >= 0 ) target[i] = removed;
                else target.Add( removed );
                continue;
            }
            if( p.Kind == "Object" && existing?.Kind == "Object" )
            {
                if( p.Comment != null ) existing.Comment = p.Comment;
                // An object that the viewed deployment writes in is its own (it can revert it); its properties keep their origin.
                if( isSelf ) existing.Origin = DeploymentOrigin;
                Apply( existing.Children!, p.Children!, isSelf );
                continue;
            }
            var replacement = new Node
            {
                Name = existing?.Name ?? p.Name,
                Kind = p.Kind,
                Value = p.Value,
                Comment = p.Comment ?? existing?.Comment,
                Origin = p.Origin,
                Children = p.Children
            };
            if( i >= 0 ) target[i] = replacement;
            else target.Add( replacement );
        }
    }

    /// <summary>
    /// The properties of the tree in document order (a property is followed by the ones of its value).
    /// </summary>
    /// <param name="withOrigin">Whether the origin of the properties is kept (the view of a deployment).</param>
    /// <returns>The properties.</returns>
    public List<EditorNodeData> Flatten( bool withOrigin )
    {
        var result = new List<EditorNodeData>();
        Flatten( Root, "", 0, withOrigin, result );
        return result;
    }

    static void Flatten( List<Node> nodes, string prefix, int depth, bool withOrigin, List<EditorNodeData> result )
    {
        foreach( var n in nodes )
        {
            var path = prefix.Length == 0 ? n.Name : $"{prefix}:{n.Name}";
            result.Add( new EditorNodeData( path, n.Name, depth, n.Kind, n.Value, n.Comment, Origin: withOrigin ? n.Origin : null ) );
            if( n.Children != null ) Flatten( n.Children, path, depth + 1, withOrigin, result );
        }
    }
}
