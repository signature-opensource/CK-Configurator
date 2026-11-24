using CK.Core;
using CK.Transform.Core;
using CK.TypeScript.Transform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Configurator;

/// <summary>
/// The document of the editor: the central configuration ("ckonf.json") of a solution, held by the backend (one document).
/// <para>
/// The text is edited with the JSON language of CK.TypeScript.Transform: an edit changes only its tokens, the comments and
/// the layout are kept. The undo history is a list of the texts before the edits. The file is read and written as bytes
/// (its encoding, with or without BOM, is kept); its SHA-256 detects a change on disk since it was read or saved.
/// </para>
/// <para>
/// The property names are case insensitive (like the configuration keys). The commands are serialized.
/// </para>
/// </summary>
public sealed class ConfiguratorEditorService : ISingletonAutoService
{
    /// <summary>
    /// The file name of the central configuration of a solution.
    /// </summary>
    public const string FileName = "ckonf.json";

    const StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
    static readonly UTF8Encoding _utf8NoBom = new( encoderShouldEmitUTF8Identifier: false );

    readonly ConfiguratorCatalogService _catalogs;
    readonly SemaphoreSlim _lock = new( 1, 1 );
    readonly List<string> _undo = new();
    readonly List<string> _redo = new();
    string? _folder;
    string? _filePath;
    string _text = "";
    string _savedText = "";
    byte[] _diskHash = [];
    bool _hasBom;

    /// <summary>
    /// Initializes a new editor service.
    /// </summary>
    /// <param name="catalogs">The opened catalogs: the one of the document types and checks it.</param>
    public ConfiguratorEditorService( ConfiguratorCatalogService catalogs )
    {
        _catalogs = catalogs;
    }

    /// <summary>
    /// Opens the "ckonf.json" of a solution folder (an opened document is closed).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="folder">The solution folder.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> OpenAsync( IActivityMonitor monitor, string folder )
    {
        return RunAsync( () =>
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( folder );
            var full = Path.GetFullPath( folder );
            var file = Path.Combine( full, FileName );
            if( !File.Exists( file ) )
            {
                Throw.CKException( $"The folder '{full}' has no '{FileName}': it is not the root of a solution with a central configuration." );
            }
            var (text, hash, bom) = Read( file );
            CheckParse( text, file );
            _folder = full;
            _filePath = file;
            SetLoaded( text, hash, bom );
            monitor.Info( $"Opened '{file}'." );
        } );
    }

    /// <summary>
    /// Gets the state.
    /// </summary>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> GetStateAsync() => RunAsync( () => { } );

    /// <summary>
    /// Closes the document (its unsaved edits are lost).
    /// </summary>
    /// <returns>The (closed) state.</returns>
    public Task<EditorSnapshot> CloseAsync()
    {
        return RunAsync( () =>
        {
            _folder = _filePath = null;
            _text = _savedText = "";
            _diskHash = [];
            _undo.Clear();
            _redo.Clear();
        } );
    }

    /// <summary>
    /// Replaces the value of an existing property.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="path">The path of the property.</param>
    /// <param name="json">The value.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SetValueAsync( IActivityMonitor monitor, string path, string json )
        => EditAsync( monitor, e => e.SetJsonValue( path, json, _comparison ) );

    /// <summary>
    /// Adds a property at the end of an object.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="objectPath">The path of the object (empty for the root object).</param>
    /// <param name="name">The property name.</param>
    /// <param name="json">The value.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> AddPropertyAsync( IActivityMonitor monitor, string objectPath, string name, string json )
        => EditAsync( monitor, e => e.AddJsonProperty( objectPath, name, json, _comparison ) );

    /// <summary>
    /// Removes a property.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="path">The path of the property.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> RemovePropertyAsync( IActivityMonitor monitor, string path )
        => EditAsync( monitor, e => e.RemoveJsonProperty( path, _comparison ) );

    /// <summary>
    /// Sets the source of the catalog of the document: its <c>"$configurator"</c> root key (an edit).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="world">The World.</param>
    /// <param name="version">The version of the profile.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SetCatalogSourceAsync( IActivityMonitor monitor, string world, string version )
    {
        var value = System.Text.Json.JsonSerializer.Serialize( new { World = world, Version = version } ).Replace( ",", ", " ).Replace( "{", "{ " ).Replace( "}", " }" );
        return EditAsync( monitor, e => e.EnsureJsonValue( "$configurator", value, _comparison ) );
    }

    /// <summary>
    /// Undoes the last edit (nothing when there is none).
    /// </summary>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> UndoAsync()
    {
        return RunAsync( () =>
        {
            CheckOpened();
            if( _undo.Count == 0 ) return;
            _redo.Add( _text );
            _text = _undo[^1];
            _undo.RemoveAt( _undo.Count - 1 );
        } );
    }

    /// <summary>
    /// Redoes the last undone edit (nothing when there is none).
    /// </summary>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> RedoAsync()
    {
        return RunAsync( () =>
        {
            CheckOpened();
            if( _redo.Count == 0 ) return;
            _undo.Add( _text );
            _text = _redo[^1];
            _redo.RemoveAt( _redo.Count - 1 );
        } );
    }

    /// <summary>
    /// Saves the document. A file that changed on disk is overwritten only when <paramref name="overwrite"/> is true.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="overwrite">Whether a file that changed on disk is overwritten.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SaveAsync( IActivityMonitor monitor, bool overwrite )
    {
        return RunAsync( () =>
        {
            CheckOpened();
            if( !overwrite && IsChangedOnDisk() )
            {
                Throw.CKException( $"'{_filePath}' changed on disk since it was read: reload it, or overwrite it." );
            }
            var bytes = (_hasBom ? Encoding.UTF8.GetPreamble() : []).Concat( _utf8NoBom.GetBytes( _text ) ).ToArray();
            File.WriteAllBytes( _filePath!, bytes );
            _diskHash = SHA256.HashData( bytes );
            _savedText = _text;
            monitor.Info( $"Saved '{_filePath}'." );
        } );
    }

    /// <summary>
    /// Reads the file again (the edits and their history are lost).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> ReloadAsync( IActivityMonitor monitor )
    {
        return RunAsync( () =>
        {
            CheckOpened();
            if( !File.Exists( _filePath ) ) Throw.CKException( $"'{_filePath}' doesn't exist anymore." );
            var (text, hash, bom) = Read( _filePath! );
            CheckParse( text, _filePath! );
            SetLoaded( text, hash, bom );
            monitor.Info( $"Reloaded '{_filePath}'." );
        } );
    }

    Task<EditorSnapshot> EditAsync( IActivityMonitor monitor, Func<SourceCodeEditor, bool> edit )
    {
        return RunAsync( () =>
        {
            CheckOpened();
            var analyzer = new JsonAnalyzer();
            var code = analyzer.ParseOrThrow( _text );
            string result;
            using( var editor = new SourceCodeEditor( monitor, code, analyzer ) )
            {
                if( !edit( editor ) )
                {
                    Throw.CKException( "The edit failed (see the logs)." );
                }
                result = editor.Code.ToString();
            }
            if( result == _text ) return;
            _undo.Add( _text );
            _redo.Clear();
            _text = result;
        } );
    }

    async Task<EditorSnapshot> RunAsync( Action action )
    {
        await _lock.WaitAsync().ConfigureAwait( false );
        try
        {
            action();
            return CreateSnapshot();
        }
        finally
        {
            _lock.Release();
        }
    }

    void CheckOpened()
    {
        if( _filePath == null ) Throw.CKException( "No solution is opened." );
    }

    void SetLoaded( string text, byte[] hash, bool bom )
    {
        _text = _savedText = text;
        _diskHash = hash;
        _hasBom = bom;
        _undo.Clear();
        _redo.Clear();
    }

    static (string Text, byte[] Hash, bool Bom) Read( string file )
    {
        var bytes = File.ReadAllBytes( file );
        var preamble = Encoding.UTF8.GetPreamble();
        bool bom = bytes.AsSpan().StartsWith( preamble );
        var text = _utf8NoBom.GetString( bom ? bytes.AsSpan( preamble.Length ) : bytes );
        return (text, SHA256.HashData( bytes ), bom);
    }

    static void CheckParse( string text, string file )
    {
        var r = new JsonAnalyzer().Parse( text.AsMemory() );
        if( !r.Success )
        {
            // The position of the error (a missing token at the end has no text: Token.GetSourcePosition can't be used).
            var p = r.FirstError.SourcePosition;
            Throw.CKException( $"'{file}' is not a valid JSON(C) document: {r.FirstError.ErrorMessage} (line {p.Line}, column {p.Column})." );
        }
    }

    bool IsChangedOnDisk()
    {
        if( _filePath == null ) return false;
        if( !File.Exists( _filePath ) ) return true;
        try
        {
            return !SHA256.HashData( File.ReadAllBytes( _filePath ) ).AsSpan().SequenceEqual( _diskHash );
        }
        catch( IOException )
        {
            // Being written by another process: it changes.
            return true;
        }
    }

    EditorSnapshot CreateSnapshot()
    {
        if( _filePath == null ) return EditorSnapshot.Closed;
        var code = new JsonAnalyzer().ParseOrThrow( _text );
        var nodes = new List<EditorNodeData>();
        string? catalogWorld = null;
        string? catalogVersion = null;
        var root = code.GetJsonRootObject();
        if( root != null )
        {
            Collect( code, root, "", 0, nodes );
            var c = root.FindProperty( "$configurator", _comparison )?.ObjectValue;
            if( c != null )
            {
                catalogWorld = ReadString( code, c.FindProperty( "World", _comparison ) );
                catalogVersion = ReadString( code, c.FindProperty( "Version", _comparison ) );
            }
        }
        IReadOnlyList<EditorNodeData> typed = nodes;
        IReadOnlyList<EditorProblemData> problems;
        if( catalogWorld == null || catalogVersion == null )
        {
            problems = [new EditorProblemData( "", "Info", "The document has no catalog (its \"$configurator\" root key): choose one to check the document." )];
        }
        else if( _catalogs.TryGetOpened( catalogWorld, catalogVersion ) is { } opened )
        {
            (typed, problems) = ConfigurationChecker.Check( nodes, opened.Catalog );
        }
        else
        {
            problems = [new EditorProblemData( "", "Info", $"The catalog of the document (the profile '{catalogVersion}' of '{catalogWorld}') is not opened: open it to check the document." )];
        }
        return new EditorSnapshot( _folder,
                                   _filePath,
                                   _text != _savedText,
                                   _undo.Count > 0,
                                   _redo.Count > 0,
                                   IsChangedOnDisk(),
                                   catalogWorld,
                                   catalogVersion,
                                   typed,
                                   problems );
    }

    /// <summary>
    /// Reads the properties of a document (tests).
    /// </summary>
    /// <param name="text">The JSON(C) text.</param>
    /// <returns>The properties, in document order.</returns>
    internal static IReadOnlyList<EditorNodeData> ReadNodes( string text )
    {
        var code = new JsonAnalyzer().ParseOrThrow( text );
        var nodes = new List<EditorNodeData>();
        if( code.GetJsonRootObject() is { } root ) Collect( code, root, "", 0, nodes );
        return nodes;
    }

    static string? ReadString( SourceCode code, JsonPropertySpan? p )
    {
        if( p == null || p.ValueSpan.Length != 1 ) return null;
        var t = code.Tokens[p.ValueSpan.Beg];
        return t.TokenType == TokenType.GenericString ? JsonAnalyzerUnescape( t.Text.Span ) : null;
    }

    static string JsonAnalyzerUnescape( ReadOnlySpan<char> quoted ) => System.Text.Json.JsonSerializer.Deserialize<string>( quoted )!;

    static void Collect( SourceCode code, JsonObjectSpan o, string prefix, int depth, List<EditorNodeData> nodes )
    {
        foreach( var p in o.Properties )
        {
            var path = prefix.Length == 0 ? p.Name : $"{prefix}:{p.Name}";
            string kind;
            string? value;
            if( p.ObjectValue != null )
            {
                kind = "Object";
                value = null;
            }
            else
            {
                value = code.GetJsonValueText( p );
                var first = code.Tokens[p.ValueSpan.Beg];
                kind = p.Value is JsonArraySpan
                        ? "Array"
                        : first.TokenType switch
                        {
                            TokenType.GenericString => "String",
                            TokenType.GenericNumber or TokenType.Minus => "Number",
                            _ when first.TextEquals( "null" ) => "Null",
                            _ => "Boolean"
                        };
            }
            nodes.Add( new EditorNodeData( path, p.Name, depth, kind, value, GetComment( code.Tokens[p.NameIndex] ) ) );
            if( p.ObjectValue != null ) Collect( code, p.ObjectValue, path, depth + 1, nodes );
        }
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
}

/// <summary>
/// The state of the document (see <see cref="ConfiguratorEditorService"/>).
/// </summary>
/// <param name="Folder">The solution folder, null when no document is opened.</param>
/// <param name="FilePath">The file path.</param>
/// <param name="IsDirty">Whether the document has unsaved edits.</param>
/// <param name="CanUndo">Whether an edit can be undone.</param>
/// <param name="CanRedo">Whether an undone edit can be redone.</param>
/// <param name="ChangedOnDisk">Whether the file changed on disk since it was read or saved.</param>
/// <param name="CatalogWorld">The World of the "$configurator" root key.</param>
/// <param name="CatalogVersion">The Version of the "$configurator" root key.</param>
/// <param name="Nodes">The properties, in document order.</param>
/// <param name="Problems">The problems.</param>
public sealed record EditorSnapshot( string? Folder,
                                     string? FilePath,
                                     bool IsDirty,
                                     bool CanUndo,
                                     bool CanRedo,
                                     bool ChangedOnDisk,
                                     string? CatalogWorld,
                                     string? CatalogVersion,
                                     IReadOnlyList<EditorNodeData> Nodes,
                                     IReadOnlyList<EditorProblemData> Problems )
{
    /// <summary>
    /// The state when no document is opened.
    /// </summary>
    public static readonly EditorSnapshot Closed = new( null, null, false, false, false, false, null, null, [], [] );
}

/// <summary>
/// A property of the document.
/// </summary>
/// <param name="Path">The path (names separated by ':').</param>
/// <param name="Name">The name.</param>
/// <param name="Depth">The depth (0 for a property of the root object).</param>
/// <param name="Kind">"Object", "Array", "String", "Number", "Boolean" or "Null".</param>
/// <param name="Value">The JSON text of the value (null for an object).</param>
/// <param name="Comment">The comments above the property, or null.</param>
/// <param name="Role">
/// What the property is in the configuration, given by the catalog: "Builder", "BuilderAspect", "App", "AppAspect", "Party",
/// "PartyDefaults", "PartyFeature", "PartyFeatureOption", "Client", "ClientFeature", "ClientFeatureOption", or null (a key).
/// </param>
/// <param name="TypeName">The configuration name of the configurable type of the property, or null.</param>
public sealed record EditorNodeData( string Path, string Name, int Depth, string Kind, string? Value, string? Comment, string? Role = null, string? TypeName = null );

/// <summary>
/// A problem of the document.
/// </summary>
/// <param name="Path">The path of the property (empty for the document).</param>
/// <param name="Severity">"Error", "Warning" or "Info".</param>
/// <param name="Message">The message.</param>
public sealed record EditorProblemData( string Path, string Severity, string Message );
