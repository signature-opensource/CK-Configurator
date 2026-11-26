using CK.Configurator.Catalog;
using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// What the editor shows on the properties of a view beyond their value:
/// <list type="bullet">
///   <item>
///   <b>Their lock</b>: the lowest level of the <c>"$LOCK"</c> patterns that cover them (CK.Engine.Configuration's
///   <see cref="ConfigurationPathPattern"/>: a pattern is relative to the object that holds the <c>"$LOCK"</c>; a
///   <c>"$LOCK"</c> of <c>"Defaults:App"</c> applies to every App, one in the <c>"Defaults"</c> of an App to its local
///   party too) and of the <c>[ConfigurationLock]</c> of the types of the catalog (placed on the anchors like the build:
///   <see cref="EditorAnchors"/>). A property locked by a literal pattern at its own path can be unlocked.
///   </item>
///   <item>
///   <b>The absent values that are locked</b>: a literal pattern of a <c>"$LOCK"</c> that names no property (the build
///   burns its absence at Compile) is a property of kind <see cref="AbsentKind"/> at its path, below the deepest
///   property of its path that exists (its name is the rest of the path). It can be unlocked.
///   </item>
///   <item><b>Their capability</b>: the highest level that the code supports (the <c>[ConfigurationLevel]</c> of the
///   property of the nearest configurable type), null when the catalog doesn't say.</item>
///   <item><b>Their secret</b>: the name of a handle (<c>{ "$SECRET": "..." }</c>) and, in the central view, whether
///   <c>#Dev/Secrets.json</c> has its value.</item>
/// </list>
/// </summary>
static class EditorAnnotations
{
    static readonly string[] _levels = ["Compile", "Init", "Run"];

    /// <summary>
    /// The kind of a property that is not in the document: an absent value that a literal pattern of a <c>"$LOCK"</c> locks.
    /// </summary>
    public const string AbsentKind = "Absent";

    /// <summary>
    /// The lock of a property.
    /// </summary>
    /// <param name="Level">The level: "Compile", "Init" or "Run".</param>
    /// <param name="Source">What locks it (for the user).</param>
    /// <param name="Container">The object that holds the "$LOCK" (null for a package's lock).</param>
    /// <param name="Pattern">The pattern of the "$LOCK" (null for a package's lock).</param>
    /// <param name="CanUnlock">Whether this property is the one that a literal pattern names: "Unlock" removes it.</param>
    public sealed record Lock( string Level, string Source, string? Container, string? Pattern, bool CanUnlock );

    /// <summary>
    /// Whether a path is the one of an App: "AppContexts:{name}:App" (or "Defaults:App" when <paramref name="withDefaults"/>
    /// is true). Structural: it doesn't need the catalog.
    /// </summary>
    public static bool IsAppPath( string path, bool withDefaults )
    {
        var s = path.Split( ':' );
        return (s.Length == 3 && s[0].Equals( "AppContexts", StringComparison.OrdinalIgnoreCase ) && s[2].Equals( "App", StringComparison.OrdinalIgnoreCase ))
               || (withDefaults && s.Length == 2 && s[0].Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) && s[1].Equals( "App", StringComparison.OrdinalIgnoreCase ));
    }

    /// <summary>
    /// Whether a path is the one of an AppContext aspect: "AppContexts:{name}:Aspects:{aspect}".
    /// </summary>
    public static bool IsBuilderAspectPath( string path )
    {
        var s = path.Split( ':' );
        return s.Length == 4 && s[0].Equals( "AppContexts", StringComparison.OrdinalIgnoreCase ) && s[2].Equals( "Aspects", StringComparison.OrdinalIgnoreCase )
               && !s[3].StartsWith( '$' );
    }

    /// <summary>
    /// The rank of a level ("Compile" is the lowest), -1 if it is not a level.
    /// </summary>
    public static int Rank( string? level ) => level == null ? -1 : Array.IndexOf( _levels, level );

    /// <summary>
    /// Annotates the properties of a view.
    /// </summary>
    /// <param name="nodes">The typed properties of the view.</param>
    /// <param name="catalog">The catalog (null: only the "$LOCK" and the secrets).</param>
    /// <param name="devSecrets">The names of the development secrets (central view), null in the view of a deployment.</param>
    /// <param name="locks">The locks by property path.</param>
    /// <returns>The annotated properties.</returns>
    public static List<EditorNodeData> Annotate( IReadOnlyList<EditorNodeData> nodes,
                                                 CatalogModel? catalog,
                                                 IReadOnlySet<string>? devSecrets,
                                                 out IReadOnlyDictionary<string, Lock> locks )
    {
        var found = new Dictionary<string, Lock>( StringComparer.OrdinalIgnoreCase );
        var rules = GetLockRules( nodes );
        if( catalog != null ) rules.AddRange( GetPackageLockRules( nodes, catalog ) );
        var byPath = new Dictionary<string, EditorNodeData>( StringComparer.OrdinalIgnoreCase );
        foreach( var n in nodes ) byPath.TryAdd( n.Path, n );
        var result = new List<EditorNodeData>( nodes.Count );
        for( int i = 0; i < nodes.Count; ++i )
        {
            var n = nodes[i];
            if( IsInLock( nodes, i ) )
            {
                result.Add( n );
                continue;
            }
            Lock? best = null;
            foreach( var r in rules )
            {
                if( !n.Path.StartsWith( r.Target + ":", StringComparison.OrdinalIgnoreCase ) ) continue;
                var relative = n.Path.Substring( r.Target.Length + 1 );
                if( !r.Pattern.Covers( relative ) ) continue;
                var canUnlock = r.Container != null && r.Container.Equals( r.Target, StringComparison.OrdinalIgnoreCase )
                                && r.Pattern.IsLiteral && relative.Equals( r.Pattern.Text, StringComparison.OrdinalIgnoreCase );
                var l = new Lock( r.Level, r.Source, r.Container, r.Container != null ? r.Pattern.Text : null, canUnlock );
                // The lowest level wins; at the same level, the one that can be unlocked here.
                if( best == null || Rank( l.Level ) < Rank( best.Level ) || (Rank( l.Level ) == Rank( best.Level ) && l.CanUnlock && !best.CanUnlock) )
                {
                    best = l;
                }
            }
            if( best != null ) found[n.Path] = best;
            string? secretName = null;
            string? secretStatus = null;
            if( n.Kind == "Object" && i + 1 < nodes.Count )
            {
                var c = nodes[i + 1];
                bool single = c.Depth == n.Depth + 1 && (i + 2 >= nodes.Count || nodes[i + 2].Depth <= n.Depth);
                bool isOptional = false;
                if( single && c.Kind == "String" && ConfigurationSecret.IsHandleKey( c.Name, out isOptional ) )
                {
                    secretName = JsonSerializer.Deserialize<string>( c.Value! );
                    if( devSecrets != null && secretName != null && !secretName.Contains( '{' ) )
                    {
                        secretStatus = devSecrets.Contains( secretName ) ? "Set" : isOptional ? "MissingOptional" : "Missing";
                    }
                }
            }
            result.Add( n with
            {
                LockLevel = best?.Level,
                LockSource = best?.Source,
                CanUnlock = best?.CanUnlock ?? false,
                MaxLevel = catalog != null ? GetMaxLevel( n, byPath, catalog ) : null,
                SecretName = secretName,
                SecretStatus = secretStatus
            } );
        }
        AddAbsentLocks( result, rules, found );
        locks = found;
        return result;
    }

    // The literal patterns of the "$LOCK" (at their own object, not on their fallbacks) that name no property: an absent
    // property, inserted after the last property below the deepest property of its path that exists.
    static void AddAbsentLocks( List<EditorNodeData> result, List<Rule> rules, Dictionary<string, Lock> found )
    {
        var existing = new HashSet<string>( result.Select( n => n.Path ), StringComparer.OrdinalIgnoreCase );
        var absent = new Dictionary<string, Rule>( StringComparer.OrdinalIgnoreCase );
        foreach( var r in rules )
        {
            if( r.Container == null || r.Container.Length == 0 || !r.Container.Equals( r.Target, StringComparison.OrdinalIgnoreCase ) || !r.Pattern.IsLiteral ) continue;
            var full = $"{r.Container}:{r.Pattern.Text}";
            if( existing.Contains( full ) ) continue;
            // The lowest level wins.
            if( !absent.TryGetValue( full, out var other ) || Rank( r.Level ) < Rank( other.Level ) ) absent[full] = r;
        }
        foreach( var (full, r) in absent )
        {
            var segments = full.Split( ':' );
            int k = segments.Length - 1;
            while( k > 0 && !existing.Contains( string.Join( ':', segments, 0, k ) ) ) --k;
            if( k == 0 ) continue;
            var anchorPath = string.Join( ':', segments, 0, k );
            int index = result.FindIndex( n => n.Path.Equals( anchorPath, StringComparison.OrdinalIgnoreCase ) );
            var depth = result[index].Depth;
            int insert = index + 1;
            while( insert < result.Count && result[insert].Depth > depth ) ++insert;
            var l = new Lock( r.Level, r.Source, r.Container, r.Pattern.Text, CanUnlock: true );
            found[full] = l;
            result.Insert( insert, new EditorNodeData( full, string.Join( ':', segments, k, segments.Length - k ), depth + 1, AbsentKind, null, null )
            {
                LockLevel = l.Level,
                LockSource = l.Source,
                CanUnlock = true
            } );
        }
    }

    // A property of a "$LOCK" (its patterns are keys): not a value of the configuration.
    static bool IsInLock( IReadOnlyList<EditorNodeData> nodes, int index )
    {
        var depth = nodes[index].Depth;
        if( nodes[index].Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) ) return true;
        for( int i = index - 1; i >= 0 && depth > 0; --i )
        {
            if( nodes[i].Depth < depth )
            {
                if( nodes[i].Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) ) return true;
                depth = nodes[i].Depth;
            }
        }
        return false;
    }

    sealed record Rule( string Target, ConfigurationPathPattern Pattern, string Level, string Source, string? Container );

    // The "$LOCK" of the view: their entries follow them in document order (a pattern is a key: its ':' are not a path).
    static List<Rule> GetLockRules( IReadOnlyList<EditorNodeData> nodes )
    {
        var rules = new List<Rule>();
        var apps = nodes.Where( n => IsAppPath( n.Path, withDefaults: false ) ).Select( n => n.Path ).ToList();
        for( int i = 0; i < nodes.Count; ++i )
        {
            var l = nodes[i];
            if( !l.Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) || l.Kind != "Object" ) continue;
            var container = l.Depth == 0 ? "" : l.Path.Substring( 0, l.Path.Length - l.Name.Length - 1 );
            for( int j = i + 1; j < nodes.Count && nodes[j].Depth > l.Depth; ++j )
            {
                var e = nodes[j];
                if( e.Depth != l.Depth + 1 || e.Kind != "String" ) continue;
                var level = JsonSerializer.Deserialize<string>( e.Value! );
                if( Rank( level ) < 0 || !ConfigurationPathPattern.TryParse( e.Name, out var pattern, out _ ) ) continue;
                var source = $"the \"$LOCK\" of '{(container.Length == 0 ? "the root" : container)}' (\"{e.Name}\")";
                foreach( var target in Targets( container, apps ) )
                {
                    rules.Add( new Rule( target, pattern, level!, source, container ) );
                }
            }
        }
        return rules;
    }

    // The objects that a "$LOCK" applies to: its own; "Defaults:App..." applies to every App; the "Defaults" of an App
    // (a fallback of its local party) to its local party too.
    static IEnumerable<string> Targets( string container, List<string> apps )
    {
        var targets = new List<string> { container };
        const string defaultsApp = "Defaults:App";
        if( container.Equals( defaultsApp, StringComparison.OrdinalIgnoreCase ) || container.StartsWith( defaultsApp + ":", StringComparison.OrdinalIgnoreCase ) )
        {
            var rest = container.Substring( defaultsApp.Length );
            targets.AddRange( apps.Select( a => a + rest ) );
        }
        foreach( var t in targets.ToList() )
        {
            if( t.EndsWith( ":Defaults", StringComparison.OrdinalIgnoreCase ) ) targets.Add( t.Substring( 0, t.Length - 9 ) + ":Local" );
        }
        return targets;
    }

    static IEnumerable<Rule> GetPackageLockRules( IReadOnlyList<EditorNodeData> nodes, CatalogModel catalog )
    {
        foreach( var p in EditorAnchors.Place( nodes, catalog, t => t.Locks.Select( l => (l.Path, l) ) ) )
        {
            if( Rank( p.Item.Level ) < 0 || !ConfigurationPathPattern.TryParse( p.Pattern, out var pattern, out _ ) ) continue;
            var source = $"[ConfigurationLock( \"{p.Declaration}\" )] of '{p.Type.CSharpName}'{(p.Item.Value != null ? $" (its value: {p.Item.Value})" : "")}";
            yield return new Rule( p.AppPath, pattern, p.Item.Level, source, null );
        }
    }

    // The capability of a property: the [ConfigurationLevel] of the property of the nearest configurable type above it.
    static string? GetMaxLevel( EditorNodeData n, Dictionary<string, EditorNodeData> byPath, CatalogModel catalog )
    {
        var path = n.Path;
        while( true )
        {
            int i = path.LastIndexOf( ':' );
            if( i < 0 ) return null;
            var parent = path.Substring( 0, i );
            if( byPath.TryGetValue( parent, out var p ) && p.TypeName != null )
            {
                var property = n.Path.Substring( parent.Length + 1 ).Split( ':' )[0];
                var type = FindType( catalog, p.TypeName, p.Role );
                return type?.PropertyLevels.FirstOrDefault( l => l.Property.Equals( property, StringComparison.OrdinalIgnoreCase ) )?.Level;
            }
            path = parent;
        }
    }

    /// <summary>
    /// Finds the configurable type of a typed property (its configuration name and its role).
    /// </summary>
    public static TypeAnalysis? FindType( CatalogModel catalog, string typeName, string? role )
        => catalog.FindConfigurable( typeName ).Select( c => c.Type ).FirstOrDefault( t => KindOf( role ) == t.Configurable!.Kind );

    static string? KindOf( string? role ) => role switch
    {
        "AppContextAspect" => "AppContextAspect",
        "AppContextAspectOption" => "AppContextAspectOption",
        "AppAspect" => "AppAspect",
        "AppAspectOption" => "AppAspectOption",
        "PartyFeature" => "PartyFeature",
        "PartyFeatureOption" => "PartyFeatureOption",
        "ClientFeature" => "PartyClientFeature",
        "ClientFeatureOption" => "PartyClientFeatureOption",
        _ => null
    };
}
