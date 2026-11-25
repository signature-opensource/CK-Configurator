using CK.Core;
using CK.Setup;
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
/// The documents of the editor: the central configuration ("ckonf.json") of a solution and the overlays of its deployments
/// ("Deployment/#Production/ckonf.json"), held by the backend (one solution).
/// <para>
/// The texts are edited with the JSON language of CK.TypeScript.Transform: an edit changes only its tokens, the comments
/// and the layout are kept. The undo history is one list for the solution, in the order of the edits: the document and
/// its text before the edit (an undo shows the view of the document it changed). A file is read and written as bytes (its
/// encoding, with or without BOM, is kept); its SHA-256 detects a change on disk since it was read or saved.
/// </para>
/// <para>
/// A view is the central configuration or a deployment: the view of a deployment is its merged configuration (the
/// central one, its bases, its overlay) and its edits are written in its overlay. The property names are case insensitive
/// (like the configuration keys). The commands are serialized.
/// </para>
/// <para>
/// Once saved, the configuration is checked by the builders of the solution (<see cref="SolutionCheckRunner"/>).
/// </para>
/// </summary>
public sealed class ConfiguratorEditorService : ISingletonAutoService
{
    /// <summary>
    /// The file name of the central configuration of a solution (and of the overlay of a deployment).
    /// </summary>
    public const string FileName = "ckonf.json";

    const StringComparison _comparison = StringComparison.OrdinalIgnoreCase;
    static readonly UTF8Encoding _utf8NoBom = new( encoderShouldEmitUTF8Identifier: false );

    readonly ConfiguratorCatalogService _catalogs;
    readonly SemaphoreSlim _lock = new( 1, 1 );
    readonly SolutionCheckRunner _check = new();
    readonly List<Document> _documents = new();
    readonly List<UndoEntry> _undo = new();
    readonly List<UndoEntry> _redo = new();
    readonly List<EditorProblemData> _folderProblems = new();
    string? _folder;
    // The texts that the running (or last) check of the builders checked.
    string? _checkedTexts;

    // A document: the central configuration (Name is null) or the overlay of a deployment.
    sealed class Document
    {
        public required string? Name { get; init; }
        public required string FilePath { get; init; }
        public string Text { get; set; } = "";
        // Null for a new document (its file doesn't exist yet).
        public string? SavedText { get; set; }
        public byte[] DiskHash { get; set; } = [];
        public bool HasBom { get; set; }

        public bool IsNew => SavedText == null;
        public bool IsDirty => Text != SavedText;
    }

    // An entry of the undo (or redo) history: the document and its text before the edit (null: it didn't exist).
    sealed record UndoEntry( string? Name, string? Text );

    /// <summary>
    /// Initializes a new editor service.
    /// </summary>
    /// <param name="catalogs">The opened catalogs: the one of the solution types and checks its documents.</param>
    public ConfiguratorEditorService( ConfiguratorCatalogService catalogs )
    {
        _catalogs = catalogs;
    }

    /// <summary>
    /// Opens a solution: its "ckonf.json" and the overlays of its deployments (an opened solution is closed).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="folder">The solution folder.</param>
    /// <returns>The state of the central view.</returns>
    public Task<EditorSnapshot> OpenAsync( IActivityMonitor monitor, string folder )
    {
        return RunAsync( null, () =>
        {
            Throw.CheckNotNullOrWhiteSpaceArgument( folder );
            var full = Path.GetFullPath( folder );
            var file = Path.Combine( full, FileName );
            if( !File.Exists( file ) )
            {
                Throw.CKException( $"The folder '{full}' has no '{FileName}': it is not the root of a solution with a central configuration." );
            }
            Load( full );
            monitor.Info( $"Opened '{file}' ({_documents.Count - 1} deployment(s))." );
            return null;
        } );
    }

    /// <summary>
    /// Gets the state of a view.
    /// </summary>
    /// <param name="view">The deployment of the view, null for the central configuration.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> GetStateAsync( string? view = null ) => RunAsync( view, () => view );

    /// <summary>
    /// Closes the solution (its unsaved edits are lost).
    /// </summary>
    /// <returns>The (closed) state.</returns>
    public Task<EditorSnapshot> CloseAsync()
    {
        return RunAsync( null, () =>
        {
            _folder = null;
            _documents.Clear();
            _folderProblems.Clear();
            _undo.Clear();
            _redo.Clear();
            _check.Reset();
            _checkedTexts = null;
            return null;
        } );
    }

    /// <summary>
    /// Sets the value of a property. In the view of a deployment, the value is written in its overlay (with the objects of
    /// its path that the overlay doesn't have yet).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The deployment of the view, null for the central configuration.</param>
    /// <param name="path">The path of the property.</param>
    /// <param name="json">The value.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SetValueAsync( IActivityMonitor monitor, string? view, string path, string json )
    {
        return EditAsync( monitor, view, ( d, e ) => view == null
                                                     ? e.SetJsonValue( path, json, _comparison )
                                                     : WriteInOverlay( e, path, json ) );
    }

    /// <summary>
    /// Adds a property at the end of an object (an error if the view already has it). In the view of a deployment, it is
    /// written in its overlay.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The deployment of the view, null for the central configuration.</param>
    /// <param name="objectPath">The path of the object (empty for the root object).</param>
    /// <param name="name">The property name.</param>
    /// <param name="json">The value.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> AddPropertyAsync( IActivityMonitor monitor, string? view, string objectPath, string name, string json )
    {
        if( view == null ) return EditAsync( monitor, view, ( d, e ) => e.AddJsonProperty( objectPath, name, json, _comparison ) );
        var path = objectPath.Length == 0 ? name : $"{objectPath}:{name}";
        return EditAsync( monitor, view, ( d, e ) =>
        {
            var existing = GetMergedTree( d ).Find( path );
            if( existing != null && existing.Origin != EditorTree.RemovedOrigin )
            {
                Throw.CKException( $"The deployment '{view}' already has '{path}'." );
            }
            return WriteInOverlay( e, path, json );
        } );
    }

    /// <summary>
    /// Removes a property. In the view of a deployment, an inherited property is removed by a <c>null</c> in its overlay;
    /// a property that only the overlay writes is removed from it.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The deployment of the view, null for the central configuration.</param>
    /// <param name="path">The path of the property.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> RemovePropertyAsync( IActivityMonitor monitor, string? view, string path )
    {
        if( view == null ) return EditAsync( monitor, view, ( d, e ) => e.RemoveJsonProperty( path, _comparison ) );
        return EditAsync( monitor, view, ( d, e ) =>
        {
            if( GetInheritedTree( d ).Find( path ) != null ) return WriteInOverlay( e, path, "null" );
            return RevertInOverlay( d, e, path );
        } );
    }

    /// <summary>
    /// In the view of a deployment, removes a property from its overlay: the deployment inherits it again. The objects of
    /// its path that become empty are removed too when they have no comment and the deployment inherits an object there.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The deployment of the view.</param>
    /// <param name="path">The path of the property.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> RevertPropertyAsync( IActivityMonitor monitor, string? view, string path )
    {
        if( view == null ) Throw.CKException( "Only a property of a deployment can be reverted to the inherited one." );
        return EditAsync( monitor, view, ( d, e ) => RevertInOverlay( d, e, path ) );
    }

    /// <summary>
    /// Creates a deployment: its overlay is written at the next save.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="name">The name of the deployment ("#Staging", "Paris/Shop/#Production").</param>
    /// <param name="baseName">The deployment that it overlays, null for the central configuration.</param>
    /// <param name="writeValuesPerDeployment">
    /// Whether the values per deployment that it would inherit are written in its overlay (their inherited value): the
    /// ones that the static checks find (the markers and the declarations of the types of the catalog).
    /// </param>
    /// <returns>The state of the view of the new deployment.</returns>
    public Task<EditorSnapshot> CreateDeploymentAsync( IActivityMonitor monitor, string name, string? baseName, bool writeValuesPerDeployment = false )
    {
        return RunAsync( null, () =>
        {
            CheckOpened();
            Throw.CheckNotNullOrWhiteSpaceArgument( name );
            name = name.Replace( '\\', '/' ).Trim().Trim( '/' );
            var error = CentralDeployment.ValidateName( name, ReadDomain(), out _, out _ );
            if( error != null ) Throw.CKException( $"Invalid deployment name '{name}': {error}" );
            if( FindDocument( name ) is { } existing ) Throw.CKException( $"The deployment '{existing.Name}' already exists." );
            if( baseName != null )
            {
                var b = FindDocument( baseName ) ?? Throw.CKException<Document>( $"The deployment '{baseName}' doesn't exist." );
                baseName = b.Name;
            }
            var nl = Environment.NewLine;
            var text = $"{{{nl}"
                       + $"  // The \"{name}\" deployment: an overlay of {(baseName != null ? $"the \"{baseName}\" deployment" : "the central ckonf.json")} (a JSON Merge Patch: an object{nl}"
                       + $"  // merges, null removes, any other value, an array included, replaces). It writes the values per deployment.{nl}"
                       + (baseName != null ? $"  \"$BASE\": {System.Text.Json.JsonSerializer.Serialize( baseName )}{nl}" : "")
                       + $"}}{nl}";
            var d = new Document
            {
                Name = name,
                FilePath = Path.Combine( _folder!, CentralDeployment.FolderName, name.Replace( '/', Path.DirectorySeparatorChar ), FileName ),
                Text = text
            };
            InsertDocument( d );
            if( writeValuesPerDeployment ) WriteValuesPerDeployment( monitor, d );
            // One edit: undone, the deployment disappears.
            _undo.Add( new UndoEntry( name, null ) );
            _redo.Clear();
            monitor.Info( $"Deployment '{name}' created (written at the next save)." );
            return d.Name;
        } );
    }

    /// <summary>
    /// Sets the source of the catalog of the solution: the <c>"$configurator"</c> root key of its central configuration
    /// (an edit).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The view of the returned state.</param>
    /// <param name="world">The World.</param>
    /// <param name="version">The version of the profile.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SetCatalogSourceAsync( IActivityMonitor monitor, string? view, string world, string version )
    {
        var value = System.Text.Json.JsonSerializer.Serialize( new { World = world, Version = version } ).Replace( ",", ", " ).Replace( "{", "{ " ).Replace( "}", " }" );
        return EditAsync( monitor, null, ( d, e ) => e.EnsureJsonValue( "$configurator", value, _comparison ), view );
    }

    /// <summary>
    /// Undoes the last edit (nothing when there is none).
    /// </summary>
    /// <returns>The state of the view of the changed document.</returns>
    public Task<EditorSnapshot> UndoAsync() => RunAsync( null, () => Swap( _undo, _redo ) );

    /// <summary>
    /// Redoes the last undone edit (nothing when there is none).
    /// </summary>
    /// <returns>The state of the view of the changed document.</returns>
    public Task<EditorSnapshot> RedoAsync() => RunAsync( null, () => Swap( _redo, _undo ) );

    // Restores the text of the last entry of "from" and pushes the current one in "to". Returns the view to show.
    string? Swap( List<UndoEntry> from, List<UndoEntry> to )
    {
        CheckOpened();
        if( from.Count == 0 ) return null;
        var entry = from[^1];
        from.RemoveAt( from.Count - 1 );
        var d = FindDocument( entry.Name );
        to.Add( new UndoEntry( entry.Name, d?.Text ) );
        if( entry.Text == null )
        {
            // Back before the creation of the document.
            if( d != null ) _documents.Remove( d );
            return null;
        }
        if( d == null )
        {
            // Back after the creation of the document.
            InsertDocument( d = new Document
            {
                Name = entry.Name,
                FilePath = Path.Combine( _folder!, CentralDeployment.FolderName, entry.Name!.Replace( '/', Path.DirectorySeparatorChar ), FileName )
            } );
        }
        d.Text = entry.Text;
        return d.Name;
    }

    /// <summary>
    /// Saves the documents that have unsaved edits. When one of them changed on disk, nothing is saved unless
    /// <paramref name="overwrite"/> is true. Once saved, the builders of the solution check the configuration.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The view of the returned state.</param>
    /// <param name="overwrite">Whether a file that changed on disk is overwritten.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> SaveAsync( IActivityMonitor monitor, string? view, bool overwrite )
    {
        return RunAsync( view, () =>
        {
            CheckOpened();
            var dirty = _documents.Where( d => d.IsDirty ).ToList();
            if( !overwrite && dirty.FirstOrDefault( IsChangedOnDisk ) is { } changed )
            {
                Throw.CKException( $"'{changed.FilePath}' changed on disk since it was read: reload the solution, or overwrite it." );
            }
            foreach( var d in dirty )
            {
                var bytes = (d.HasBom ? Encoding.UTF8.GetPreamble() : []).Concat( _utf8NoBom.GetBytes( d.Text ) ).ToArray();
                Directory.CreateDirectory( Path.GetDirectoryName( d.FilePath )! );
                File.WriteAllBytes( d.FilePath, bytes );
                d.DiskHash = SHA256.HashData( bytes );
                d.SavedText = d.Text;
                monitor.Info( $"Saved '{d.FilePath}'." );
            }
            StartCheck();
            return view;
        } );
    }

    /// <summary>
    /// Checks the saved configuration again with the builders of the solution.
    /// </summary>
    /// <param name="view">The view of the returned state.</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> CheckAsync( string? view )
    {
        return RunAsync( view, () =>
        {
            CheckOpened();
            StartCheck();
            return view;
        } );
    }

    void StartCheck()
    {
        var builders = EditorTree.Parse( GetCentral().Text ).Find( "Builders" )?.Children?.Select( b => b.Name ).ToList() ?? [];
        _checkedTexts = SavedTexts();
        _check.Start( _folder!, builders );
    }

    string SavedTexts() => string.Join( "\0", _documents.Select( d => $"{d.Name}\0{d.SavedText}" ) );

    /// <summary>
    /// Reads the documents again and finds the deployments again (the edits and their history are lost).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="view">The view of the returned state (the central one if the deployment doesn't exist anymore).</param>
    /// <returns>The state.</returns>
    public Task<EditorSnapshot> ReloadAsync( IActivityMonitor monitor, string? view )
    {
        return RunAsync( view, () =>
        {
            CheckOpened();
            if( !File.Exists( Path.Combine( _folder!, FileName ) ) ) Throw.CKException( $"'{Path.Combine( _folder!, FileName )}' doesn't exist anymore." );
            Load( _folder! );
            monitor.Info( $"Reloaded '{_folder}'." );
            return view;
        } );
    }

    void Load( string folder )
    {
        var central = new Document { Name = null, FilePath = Path.Combine( folder, FileName ) };
        Read( central );
        var documents = new List<Document> { central };
        var problems = new List<EditorProblemData>();
        foreach( var f in CentralDeployment.DiscoverFolders( folder, ReadDomain( central.Text ) ) )
        {
            if( f.Error != null )
            {
                problems.Add( new EditorProblemData( "", "Error", $"Invalid deployment folder '{f.Folder}': {f.Error}" ) );
                continue;
            }
            var d = new Document { Name = f.Name, FilePath = Path.GetFullPath( f.Folder.AppendPart( FileName ) ) };
            if( !File.Exists( d.FilePath ) )
            {
                problems.Add( new EditorProblemData( "", "Error", $"The deployment folder '{f.Folder}' has no '{FileName}'." ) );
                continue;
            }
            Read( d );
            documents.Add( d );
        }
        _folder = folder;
        _documents.Clear();
        _documents.AddRange( documents );
        _folderProblems.Clear();
        _folderProblems.AddRange( problems );
        _undo.Clear();
        _redo.Clear();
        _check.Reset();
        _checkedTexts = null;
    }

    static void Read( Document d )
    {
        var bytes = File.ReadAllBytes( d.FilePath );
        var preamble = Encoding.UTF8.GetPreamble();
        bool bom = bytes.AsSpan().StartsWith( preamble );
        var text = _utf8NoBom.GetString( bom ? bytes.AsSpan( preamble.Length ) : bytes );
        var r = new JsonAnalyzer().Parse( text.AsMemory() );
        if( !r.Success )
        {
            // The position of the error (a missing token at the end has no text: Token.GetSourcePosition can't be used).
            var p = r.FirstError.SourcePosition;
            Throw.CKException( $"'{d.FilePath}' is not a valid JSON(C) document: {r.FirstError.ErrorMessage} (line {p.Line}, column {p.Column})." );
        }
        d.Text = d.SavedText = text;
        d.DiskHash = SHA256.HashData( bytes );
        d.HasBom = bom;
    }

    string? ReadDomain() => ReadDomain( GetCentral().Text );

    static string? ReadDomain( string centralText )
    {
        var v = EditorTree.Parse( centralText ).Find( "Domain" );
        return v?.Kind == "String" ? System.Text.Json.JsonSerializer.Deserialize<string>( v.Value! ) : null;
    }

    Document GetCentral() => _documents[0];

    Document? FindDocument( string? name ) => _documents.FirstOrDefault( d => string.Equals( d.Name, name, _comparison ) );

    void InsertDocument( Document d )
    {
        int i = 1;
        while( i < _documents.Count && string.Compare( _documents[i].Name, d.Name, _comparison ) < 0 ) ++i;
        _documents.Insert( i, d );
    }

    Document GetDocument( string? view )
    {
        CheckOpened();
        return FindDocument( view ) ?? Throw.CKException<Document>( $"The deployment '{view}' doesn't exist." );
    }

    Task<EditorSnapshot> EditAsync( IActivityMonitor monitor, string? view, Func<Document, SourceCodeEditor, bool> edit, string? resultView = null )
    {
        resultView ??= view;
        return RunAsync( resultView, () =>
        {
            var d = GetDocument( view );
            var analyzer = new JsonAnalyzer();
            var code = analyzer.ParseOrThrow( d.Text );
            string result;
            using( var editor = new SourceCodeEditor( monitor, code, analyzer ) )
            {
                if( !edit( d, editor ) )
                {
                    Throw.CKException( "The edit failed (see the logs)." );
                }
                result = editor.Code.ToString();
            }
            if( result != d.Text )
            {
                _undo.Add( new UndoEntry( d.Name, d.Text ) );
                _redo.Clear();
                d.Text = result;
            }
            return resultView;
        } );
    }

    // Writes in the overlay of a deployment the inherited values per deployment (the problems that "WriteHere" fixes),
    // with their current value in its view.
    void WriteValuesPerDeployment( IActivityMonitor monitor, Document d )
    {
        var centralTree = EditorTree.Parse( GetCentral().Text );
        var (catalog, world, version) = GetCatalog( centralTree );
        var (nodes, problems) = CreateView( d, centralTree, catalog, world, version );
        var values = problems.Where( p => p.Fix == EditorProblemData.WriteHereFix )
                             .Select( p => nodes.FirstOrDefault( n => n.Path.Equals( p.Path, _comparison ) ) )
                             .Where( n => n?.Value != null )
                             .ToList();
        foreach( var n in values )
        {
            var analyzer = new JsonAnalyzer();
            using var editor = new SourceCodeEditor( monitor, analyzer.ParseOrThrow( d.Text ), analyzer );
            if( !WriteInOverlay( editor, n!.Path, n.Value! ) ) Throw.CKException( $"Unable to write '{n.Path}' in the deployment '{d.Name}' (see the logs)." );
            d.Text = editor.Code.ToString();
        }
        monitor.Info( $"{values.Count} value(s) per deployment written in '{d.Name}'." );
    }

    // The catalog of the solution (its "$configurator"), when it is opened.
    (Catalog.Catalog? Catalog, string? World, string? Version) GetCatalog( EditorTree centralTree )
    {
        string? world = null;
        string? version = null;
        if( centralTree.Find( "$configurator" ) is { Kind: "Object" } c )
        {
            world = ReadString( c.Find( "World" ) );
            version = ReadString( c.Find( "Version" ) );
        }
        var catalog = world != null && version != null ? _catalogs.TryGetOpened( world, version )?.Catalog : null;
        return (catalog, world, version);
    }

    // Writes a value in an overlay: the objects of its path that the overlay doesn't have are added with it.
    static bool WriteInOverlay( SourceCodeEditor e, string path, string json )
    {
        var segments = path.Split( ':' );
        var o = e.Code.GetJsonRootObject() ?? Throw.CKException<JsonObjectSpan>( "The overlay is not a JSON object." );
        for( int i = 0; i < segments.Length; ++i )
        {
            var p = o.FindProperty( segments[i], _comparison );
            if( p == null )
            {
                var value = json;
                for( int j = segments.Length - 1; j > i; --j )
                {
                    value = $"{{ {System.Text.Json.JsonSerializer.Serialize( segments[j] )}: {value} }}";
                }
                return e.AddJsonProperty( string.Join( ':', segments, 0, i ), segments[i], value, _comparison );
            }
            if( i == segments.Length - 1 ) return e.SetJsonValue( path, json, _comparison );
            if( p.ObjectValue == null )
            {
                Throw.CKException( $"The overlay replaces '{string.Join( ':', segments, 0, i + 1 )}' as a whole: edit its value." );
            }
            o = p.ObjectValue;
        }
        return false;
    }

    // Removes a property from the overlay, then the objects of its path that became empty "{}" (no comment) when the
    // deployment inherits an object there (an empty object would otherwise create it).
    bool RevertInOverlay( Document d, SourceCodeEditor e, string path )
    {
        if( e.Code.FindJsonProperty( path, _comparison ) == null )
        {
            Throw.CKException( $"The deployment '{d.Name}' doesn't write '{path}': it inherits it." );
        }
        if( !e.RemoveJsonProperty( path, _comparison ) ) return false;
        var inherited = GetInheritedTree( d );
        var parent = path;
        while( true )
        {
            int i = parent.LastIndexOf( ':' );
            if( i < 0 ) break;
            parent = parent.Substring( 0, i );
            var p = e.Code.FindJsonProperty( parent, _comparison );
            if( p?.ObjectValue == null || e.Code.GetJsonValueText( p ).Trim() != "{}" ) break;
            if( inherited.Find( parent )?.Kind != "Object" ) break;
            if( !e.RemoveJsonProperty( parent, _comparison ) ) return false;
        }
        return true;
    }

    // The layers of a deployment: its bases, the farthest first. Null (and the problem) when its "$BASE" is invalid.
    List<(Document Base, EditorTree Tree)>? GetBases( Document d, List<EditorProblemData>? problems )
    {
        var result = new List<(Document, EditorTree)>();
        var visited = new List<string> { d.Name! };
        var current = d;
        while( true )
        {
            var b = EditorTree.Parse( current.Text ).Root.FirstOrDefault( n => n.Name.Equals( CentralDeployment.BaseKey, _comparison ) );
            if( b == null ) break;
            // The problems of the chain are on the "$BASE" of the view.
            var path = CentralDeployment.BaseKey;
            string? baseName = b.Kind == "String" ? System.Text.Json.JsonSerializer.Deserialize<string>( b.Value! )?.Replace( '\\', '/' ).Trim().Trim( '/' ) : null;
            if( string.IsNullOrWhiteSpace( baseName ) )
            {
                problems?.Add( new EditorProblemData( path, "Error", $"Invalid \"{CentralDeployment.BaseKey}\" of '{current.Name}': the name of a deployment (\"#Production\") is expected." ) );
                return null;
            }
            var baseDocument = FindDocument( baseName );
            if( baseDocument == null )
            {
                problems?.Add( new EditorProblemData( path, "Error", $"The \"{CentralDeployment.BaseKey}\" of the deployment '{current.Name}' is '{baseName}': there is no such deployment ('{string.Join( "', '", _documents.Skip( 1 ).Select( x => x.Name ) )}')." ) );
                return null;
            }
            if( visited.Contains( baseDocument.Name!, StringComparer.OrdinalIgnoreCase ) )
            {
                problems?.Add( new EditorProblemData( path, "Error", $"The deployments '{string.Join( "' -> '", visited )}' -> '{baseDocument.Name}' overlay each other (\"{CentralDeployment.BaseKey}\")." ) );
                return null;
            }
            visited.Add( baseDocument.Name! );
            result.Insert( 0, (baseDocument, EditorTree.Parse( baseDocument.Text )) );
            current = baseDocument;
        }
        return result;
    }

    // The merged view of a deployment.
    EditorTree GetMergedTree( Document d )
    {
        var bases = GetBases( d, null ) ?? [];
        return EditorTree.Merge( EditorTree.Parse( GetCentral().Text ), bases.Select( b => (b.Base.Name!, b.Tree) ), EditorTree.Parse( d.Text ) );
    }

    // What a deployment inherits: the merged view of its bases, without its own overlay.
    EditorTree GetInheritedTree( Document d )
    {
        var bases = GetBases( d, null ) ?? [];
        return EditorTree.Merge( EditorTree.Parse( GetCentral().Text ), bases.Select( b => (b.Base.Name!, b.Tree) ), new EditorTree( [] ) );
    }

    async Task<EditorSnapshot> RunAsync( string? view, Func<string?> action )
    {
        await _lock.WaitAsync().ConfigureAwait( false );
        try
        {
            view = action();
            return CreateSnapshot( view );
        }
        finally
        {
            _lock.Release();
        }
    }

    void CheckOpened()
    {
        if( _folder == null ) Throw.CKException( "No solution is opened." );
    }

    static bool IsChangedOnDisk( Document d )
    {
        if( d.IsNew ) return false;
        if( !File.Exists( d.FilePath ) ) return true;
        try
        {
            return !SHA256.HashData( File.ReadAllBytes( d.FilePath ) ).AsSpan().SequenceEqual( d.DiskHash );
        }
        catch( IOException )
        {
            // Being written by another process: it changes.
            return true;
        }
    }

    EditorSnapshot CreateSnapshot( string? view )
    {
        if( _folder == null ) return EditorSnapshot.Closed;
        var current = FindDocument( view ) ?? GetCentral();
        var centralTree = EditorTree.Parse( GetCentral().Text );
        var (catalog, catalogWorld, catalogVersion) = GetCatalog( centralTree );
        var (checkStatus, checkResults) = _check.Get();
        var documents = new List<EditorDocumentData>();
        IReadOnlyList<EditorNodeData> nodes = [];
        IReadOnlyList<EditorProblemData> problems = [];
        foreach( var d in _documents )
        {
            var (dNodes, dProblems) = CreateView( d, centralTree, catalog, catalogWorld, catalogVersion );
            dProblems.AddRange( GetBuildProblems( d, dNodes, checkResults ) );
            if( d == current )
            {
                nodes = dNodes;
                problems = dProblems;
            }
            documents.Add( new EditorDocumentData( d.Name,
                                                   d.Name != null ? ReadBaseName( d ) : null,
                                                   d.FilePath,
                                                   d.IsDirty,
                                                   d.IsNew,
                                                   IsChangedOnDisk( d ),
                                                   dProblems.Count( p => p.Severity == "Error" ),
                                                   dProblems.Count( p => p.Severity == "Warning" ) ) );
        }
        return new EditorSnapshot( _folder,
                                   current.Name,
                                   current.FilePath,
                                   documents.Any( d => d.IsDirty ),
                                   _undo.Count > 0,
                                   _redo.Count > 0,
                                   documents.Any( d => d.ChangedOnDisk ),
                                   catalogWorld,
                                   catalogVersion,
                                   checkStatus,
                                   checkStatus != "None" && _checkedTexts != null && (_checkedTexts != SavedTexts() || _documents.Any( d => d.IsDirty )),
                                   documents,
                                   nodes,
                                   problems );
    }

    // The nodes and the static problems of the view of a document.
    (IReadOnlyList<EditorNodeData> Nodes, List<EditorProblemData> Problems) CreateView( Document d,
                                                                                        EditorTree centralTree,
                                                                                        Catalog.Catalog? catalog,
                                                                                        string? catalogWorld,
                                                                                        string? catalogVersion )
    {
        var problems = new List<EditorProblemData>();
        IReadOnlyList<EditorNodeData> nodes;
        if( d.Name == null )
        {
            nodes = centralTree.Flatten( withOrigin: false );
            problems.AddRange( _folderProblems );
            if( catalog != null )
            {
                (nodes, var p) = ConfigurationChecker.Check( nodes, catalog );
                problems.AddRange( p );
            }
        }
        else
        {
            var bases = GetBases( d, problems ) ?? [];
            var selfTree = EditorTree.Parse( d.Text );
            var merged = EditorTree.Merge( centralTree, bases.Select( b => (b.Base.Name!, b.Tree) ), selfTree );
            nodes = merged.Flatten( withOrigin: true );
            if( catalog != null )
            {
                // The removed properties are not in the merged configuration.
                var (typed, p) = ConfigurationChecker.Check( nodes.Where( n => n.Origin != EditorTree.RemovedOrigin ).ToList(), catalog, isDeployment: true );
                var roles = typed.ToDictionary( n => n.Path, StringComparer.OrdinalIgnoreCase );
                nodes = nodes.Select( n => roles.TryGetValue( n.Path, out var t ) ? n with { Role = t.Role, TypeName = t.TypeName } : n ).ToList();
                problems.AddRange( p );
            }
            DeploymentChecker.Check( GetCentral().Text,
                                     bases.Select( b => new DeploymentChecker.Layer( b.Base.Name!, b.Base.Text ) ).ToList(),
                                     new DeploymentChecker.Layer( d.Name, d.Text ),
                                     selfTree,
                                     nodes.Where( n => n.Origin != EditorTree.RemovedOrigin ).ToList(),
                                     catalog,
                                     problems );
        }
        if( catalogWorld == null || catalogVersion == null )
        {
            problems.Add( new EditorProblemData( "", "Info", "The solution has no catalog (the \"$configurator\" root key of its central configuration): choose one to check the documents." ) );
        }
        else if( catalog == null )
        {
            problems.Add( new EditorProblemData( "", "Info", $"The catalog of the solution (the profile '{catalogVersion}' of '{catalogWorld}') is not opened: the documents are not checked." ) );
        }
        return (nodes, problems);
    }

    string? ReadBaseName( Document d )
    {
        var b = EditorTree.Parse( d.Text ).Root.FirstOrDefault( n => n.Name.Equals( CentralDeployment.BaseKey, _comparison ) );
        return b?.Kind == "String" ? System.Text.Json.JsonSerializer.Deserialize<string>( b.Value! ) : null;
    }

    static string? ReadString( EditorTree.Node? n ) => n?.Kind == "String" ? System.Text.Json.JsonSerializer.Deserialize<string>( n.Value! ) : null;

    // The problems of the check by the builders that belong to a view: the ones about a deployment ("The deployment
    // '#Production' ...") to its view, the others to the central one. A problem is put on the property that starts its
    // message ("\"Builders:...\": ..." or a path of the projection of the builder), otherwise on its builder.
    static IEnumerable<EditorProblemData> GetBuildProblems( Document d, IReadOnlyList<EditorNodeData> nodes, IReadOnlyList<SolutionCheckRunner.BuilderCheck> results )
    {
        if( results.Count == 0 ) yield break;
        var paths = new HashSet<string>( nodes.Select( n => n.Path ), StringComparer.OrdinalIgnoreCase );
        foreach( var r in results )
        {
            var builderPath = r.Builder.Length > 0 ? $"Builders:{r.Builder}" : "";
            if( !paths.Contains( builderPath ) ) builderPath = "";
            if( r.Failure != null )
            {
                if( d.Name == null ) yield return new EditorProblemData( builderPath, "Error", $"The builder '{r.Builder}' can't check the configuration: {r.Failure}", "Build" );
                continue;
            }
            foreach( var (level, text) in r.Entries )
            {
                var deployment = DeploymentOf( text );
                if( !string.Equals( deployment, d.Name, StringComparison.OrdinalIgnoreCase ) ) continue;
                var severity = level is "Error" or "Fatal" ? "Error" : "Warning";
                yield return new EditorProblemData( FindPath( paths, text, r.Builder ) ?? builderPath, severity, $"{r.Builder}: {text}", "Build" );
            }
        }
    }

    static string? DeploymentOf( string text )
    {
        const string prefix = "The deployment '";
        int i = text.IndexOf( prefix, StringComparison.Ordinal );
        if( i < 0 ) return null;
        int end = text.IndexOf( '\'', i + prefix.Length );
        return end < 0 ? null : text.Substring( i + prefix.Length, end - i - prefix.Length );
    }

    // The first quoted configuration path of a message ("\"CK-AppIdentity:Local:X\": ..." or "inherits \"Builders:...\""),
    // mapped to the deepest property of the view: as is, in the builder, or in its App for a path of the projection
    // of the parties ("CK-AppIdentity:...").
    static string? FindPath( HashSet<string> paths, string text, string builder )
    {
        int start = text.IndexOf( '"' );
        if( start < 0 ) return null;
        int end = text.IndexOf( '"', start + 1 );
        if( end < 0 ) return null;
        var quoted = text.Substring( start + 1, end - start - 1 );
        if( !quoted.Contains( ':' ) || quoted.Contains( ' ' ) ) return null;
        string[] candidates = quoted.StartsWith( "CK-AppIdentity:", StringComparison.OrdinalIgnoreCase )
                                ? [$"Builders:{builder}:App:{quoted.Substring( 15 )}"]
                                : [quoted, $"Builders:{builder}:{quoted}", $"Builders:{builder}:App:{quoted}"];
        string? best = null;
        foreach( var c in candidates )
        {
            var p = c;
            while( p.Length > 0 && !paths.Contains( p ) )
            {
                int i = p.LastIndexOf( ':' );
                p = i < 0 ? "" : p.Substring( 0, i );
            }
            if( p.Length > (best?.Length ?? 0) ) best = p;
        }
        return best;
    }

    /// <summary>
    /// Reads the properties of a document (tests).
    /// </summary>
    /// <param name="text">The JSON(C) text.</param>
    /// <returns>The properties, in document order.</returns>
    internal static IReadOnlyList<EditorNodeData> ReadNodes( string text ) => EditorTree.Parse( text ).Flatten( withOrigin: false );
}

/// <summary>
/// The state of a view of the solution (see <see cref="ConfiguratorEditorService"/>).
/// </summary>
/// <param name="Folder">The solution folder, null when no solution is opened.</param>
/// <param name="View">The deployment of the view, null for the central configuration.</param>
/// <param name="FilePath">The file path of the view.</param>
/// <param name="IsDirty">Whether a document has unsaved edits.</param>
/// <param name="CanUndo">Whether an edit can be undone.</param>
/// <param name="CanRedo">Whether an undone edit can be redone.</param>
/// <param name="ChangedOnDisk">Whether a file changed on disk since it was read or saved.</param>
/// <param name="CatalogWorld">The World of the "$configurator" root key.</param>
/// <param name="CatalogVersion">The Version of the "$configurator" root key.</param>
/// <param name="CheckStatus">The check by the builders: "None", "Running", "Succeeded" or "Failed".</param>
/// <param name="CheckIsStale">Whether the documents changed since the check.</param>
/// <param name="Documents">The documents of the solution.</param>
/// <param name="Nodes">The properties of the view, in document order.</param>
/// <param name="Problems">The problems of the view.</param>
public sealed record EditorSnapshot( string? Folder,
                                     string? View,
                                     string? FilePath,
                                     bool IsDirty,
                                     bool CanUndo,
                                     bool CanRedo,
                                     bool ChangedOnDisk,
                                     string? CatalogWorld,
                                     string? CatalogVersion,
                                     string CheckStatus,
                                     bool CheckIsStale,
                                     IReadOnlyList<EditorDocumentData> Documents,
                                     IReadOnlyList<EditorNodeData> Nodes,
                                     IReadOnlyList<EditorProblemData> Problems )
{
    /// <summary>
    /// The state when no solution is opened.
    /// </summary>
    public static readonly EditorSnapshot Closed = new( null, null, null, false, false, false, false, null, null, "None", false, [], [], [] );
}

/// <summary>
/// A document of the solution.
/// </summary>
/// <param name="Name">The name of the deployment, null for the central configuration.</param>
/// <param name="Base">The deployment that it overlays, or null.</param>
/// <param name="FilePath">The file path.</param>
/// <param name="IsDirty">Whether it has unsaved edits.</param>
/// <param name="IsNew">Whether its file is written at the next save.</param>
/// <param name="ChangedOnDisk">Whether its file changed on disk since it was read or saved.</param>
/// <param name="Errors">The number of errors of its view.</param>
/// <param name="Warnings">The number of warnings of its view.</param>
public sealed record EditorDocumentData( string? Name, string? Base, string FilePath, bool IsDirty, bool IsNew, bool ChangedOnDisk, int Errors, int Warnings );

/// <summary>
/// A property of a view.
/// </summary>
/// <param name="Path">The path (names separated by ':').</param>
/// <param name="Name">The name.</param>
/// <param name="Depth">The depth (0 for a property of the root object).</param>
/// <param name="Kind">"Object", "Array", "String", "Number", "Boolean" or "Null".</param>
/// <param name="Value">The JSON text of the value (null for an object).</param>
/// <param name="Comment">The comments above the property, or null.</param>
/// <param name="Role">
/// What the property is in the configuration, given by the catalog: "Builder", "BuilderAspect", "BuilderAspectOption",
/// "App", "AppAspect", "AppAspectOption", "Party", "PartyDefaults", "PartyFeature", "PartyFeatureOption", "Client",
/// "ClientFeature", "ClientFeatureOption", or null (a key).
/// </param>
/// <param name="TypeName">The configuration name of the configurable type of the property, or null.</param>
/// <param name="Origin">
/// Where the property comes from in the view of a deployment (null in the central view): "Central", the name of a base
/// deployment, "Deployment" (written by its overlay) or "Removed" (a <c>null</c> of its overlay removes it).
/// </param>
public sealed record EditorNodeData( string Path,
                                     string Name,
                                     int Depth,
                                     string Kind,
                                     string? Value,
                                     string? Comment,
                                     string? Role = null,
                                     string? TypeName = null,
                                     string? Origin = null );

/// <summary>
/// A problem of a view.
/// </summary>
/// <param name="Path">The path of the property (empty for the document).</param>
/// <param name="Severity">"Error", "Warning" or "Info".</param>
/// <param name="Message">The message.</param>
/// <param name="Layer">"Static" (the rules, from the catalog) or "Build" (the check by the builders).</param>
/// <param name="Fix">The fix that the editor offers, or null: <see cref="WriteHereFix"/>.</param>
public sealed record EditorProblemData( string Path, string Severity, string Message, string Layer = "Static", string? Fix = null )
{
    /// <summary>
    /// The fix of an inherited value per deployment: the inherited value of the property is written in the overlay of
    /// the deployment (a <see cref="ConfiguratorEditorService.SetValueAsync"/> of its current value in the view).
    /// </summary>
    public const string WriteHereFix = "WriteHere";
}
