using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace CK.Configurator.Catalog.Worker;

/// <summary>
/// Finds the raw URL of the source file of a type: the documents of the type in the PDB of its assembly (embedded, or
/// the ".pdb" file next to it) mapped by its SourceLink. The source file of a type is the document of its methods, of
/// its <c>TypeDefinitionDocuments</c> for a type without method bodies (an interface): the one named after the type
/// when it has several (a partial type).
/// </summary>
sealed class SourceLocator
{
    static readonly Guid _sourceLink = new( "CC110556-A091-4D38-9FEC-25AB9A351A6A" );
    static readonly Guid _typeDocuments = new( "932E74BC-DBA9-4478-8D46-0F32A7BAB3D3" );

    readonly IActivityMonitor _monitor;
    // The raw URL by type metadata token, per assembly (null when the assembly has no PDB or no SourceLink).
    readonly Dictionary<Assembly, Dictionary<int, string>?> _assemblies = new();

    public SourceLocator( IActivityMonitor monitor ) => _monitor = monitor;

    /// <summary>
    /// Gets the raw URL of the source file of a type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The raw URL or null when it is unknown.</returns>
    public string? GetSourceUrl( Type type )
    {
        if( type.IsConstructedGenericType ) type = type.GetGenericTypeDefinition();
        if( !_assemblies.TryGetValue( type.Assembly, out var urls ) )
        {
            _assemblies.Add( type.Assembly, urls = Read( type.Assembly ) );
        }
        return urls != null && urls.TryGetValue( type.MetadataToken, out var url ) ? url : null;
    }

    Dictionary<int, string>? Read( Assembly assembly )
    {
        if( string.IsNullOrEmpty( assembly.Location ) ) return null;
        try
        {
            using var pe = new PEReader( File.OpenRead( assembly.Location ) );
            using var provider = OpenPdb( pe, assembly.Location );
            if( provider == null ) return null;
            var pdb = provider.GetMetadataReader();
            var map = ReadSourceLink( pdb );
            if( map.Count == 0 ) return null;
            var md = pe.GetMetadataReader();
            // The documents of each type: its methods, then its TypeDefinitionDocuments.
            var documents = new Dictionary<TypeDefinitionHandle, HashSet<DocumentHandle>>();
            foreach( var th in md.TypeDefinitions )
            {
                foreach( var mh in md.GetTypeDefinition( th ).GetMethods() )
                {
                    var mdi = pdb.GetMethodDebugInformation( mh.ToDebugInformationHandle() );
                    if( !mdi.Document.IsNil )
                    {
                        Add( documents, th, mdi.Document );
                    }
                    else if( !mdi.SequencePointsBlob.IsNil )
                    {
                        foreach( var sp in mdi.GetSequencePoints() ) Add( documents, th, sp.Document );
                    }
                }
            }
            foreach( var h in pdb.CustomDebugInformation )
            {
                var cdi = pdb.GetCustomDebugInformation( h );
                if( cdi.Parent.Kind != HandleKind.TypeDefinition || pdb.GetGuid( cdi.Kind ) != _typeDocuments ) continue;
                var blob = pdb.GetBlobReader( cdi.Value );
                while( blob.RemainingBytes > 0 )
                {
                    Add( documents, (TypeDefinitionHandle)cdi.Parent, MetadataTokens.DocumentHandle( blob.ReadCompressedInteger() ) );
                }
            }
            var result = new Dictionary<int, string>();
            foreach( var (th, docs) in documents )
            {
                var path = Choose( md, th, docs.Select( d => pdb.GetString( pdb.GetDocument( d ).Name ) ) );
                var url = path != null ? Map( map, path ) : null;
                if( url != null ) result.Add( MetadataTokens.GetToken( th ), url );
            }
            return result;
        }
        catch( Exception ex )
        {
            _monitor.Warn( $"Unable to read the source files of '{assembly.GetName().Name}'.", ex );
            return null;
        }

        static void Add( Dictionary<TypeDefinitionHandle, HashSet<DocumentHandle>> documents, TypeDefinitionHandle t, DocumentHandle d )
        {
            if( !documents.TryGetValue( t, out var set ) ) documents.Add( t, set = [] );
            set.Add( d );
        }
    }

    static MetadataReaderProvider? OpenPdb( PEReader pe, string location )
    {
        var embedded = pe.ReadDebugDirectory().FirstOrDefault( e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb );
        if( embedded.DataSize > 0 ) return pe.ReadEmbeddedPortablePdbDebugDirectoryData( embedded );
        var file = Path.ChangeExtension( location, ".pdb" );
        // A Windows PDB (not a portable one) is not readable: the provider throws, the caller warns.
        return File.Exists( file ) ? MetadataReaderProvider.FromPortablePdbStream( File.OpenRead( file ) ) : null;
    }

    // The SourceLink map: "/_/*" -> "https://raw.githubusercontent.com/o/r/sha/*" (or exact paths).
    static List<(string Key, bool Prefix, string Url)> ReadSourceLink( MetadataReader pdb )
    {
        var map = new List<(string Key, bool Prefix, string Url)>();
        foreach( var h in pdb.CustomDebugInformation )
        {
            var cdi = pdb.GetCustomDebugInformation( h );
            if( pdb.GetGuid( cdi.Kind ) != _sourceLink ) continue;
            using var json = JsonDocument.Parse( pdb.GetBlobBytes( cdi.Value ) );
            if( !json.RootElement.TryGetProperty( "documents", out var docs ) ) continue;
            foreach( var d in docs.EnumerateObject() )
            {
                var url = d.Value.GetString();
                if( url == null ) continue;
                if( d.Name.EndsWith( '*' ) && url.EndsWith( '*' ) )
                {
                    map.Add( (d.Name[..^1], true, url[..^1]) );
                }
                else
                {
                    map.Add( (d.Name, false, url) );
                }
            }
        }
        // The longest key first.
        map.Sort( ( a, b ) => b.Key.Length.CompareTo( a.Key.Length ) );
        return map;
    }

    static string? Map( List<(string Key, bool Prefix, string Url)> map, string path )
    {
        foreach( var (key, prefix, url) in map )
        {
            if( prefix )
            {
                if( path.StartsWith( key, StringComparison.Ordinal ) )
                {
                    return url + string.Join( '/', path[key.Length..].Replace( '\\', '/' ).Split( '/' ).Select( Uri.EscapeDataString ) );
                }
            }
            else if( path == key )
            {
                return url;
            }
        }
        return null;
    }

    // The document named after the type (or its outermost declaring type), else the first one that isn't generated
    // in an obj/ folder.
    static string? Choose( MetadataReader md, TypeDefinitionHandle th, IEnumerable<string> paths )
    {
        var all = paths.Order( StringComparer.Ordinal ).ToList();
        var t = md.GetTypeDefinition( th );
        var name = WithoutArity( md.GetString( t.Name ) );
        while( !t.GetDeclaringType().IsNil ) t = md.GetTypeDefinition( t.GetDeclaringType() );
        var outer = WithoutArity( md.GetString( t.Name ) );
        return all.FirstOrDefault( p => FileName( p ) == name )
               ?? all.FirstOrDefault( p => FileName( p ) == outer )
               ?? all.FirstOrDefault( p => !p.Replace( '\\', '/' ).Contains( "/obj/" ) );

        static string WithoutArity( string n ) { int i = n.IndexOf( '`' ); return i < 0 ? n : n[..i]; }

        static string FileName( string p ) => Path.GetFileNameWithoutExtension( p.Replace( '\\', '/' ).Split( '/' )[^1] );
    }
}
