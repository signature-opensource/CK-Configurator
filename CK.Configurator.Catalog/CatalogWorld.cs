using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CK.Configurator.Catalog;

/// <summary>
/// A World whose <c>Published</c> folder lists the profiles that a catalog can be opened on: a Stack (the URL of its
/// GitHub repository or the local folder of its clone) and, for a Long Term Support World, its LTS name.
/// <list type="bullet">
///   <item><c>https://github.com/signature-opensource/CK-Stack</c> is the default World (its <c>Published</c> folder);</item>
///   <item><c>https://github.com/signature-opensource/CK-Stack/tree/main/@net8</c> is the <c>CK@net8</c> LTS World
///   (its <c>@net8/Published</c> folder);</item>
///   <item>a local folder is a clone of a Stack, or its <c>@net8</c> sub folder for an LTS World.</item>
/// </list>
/// The <c>main</c> branch of a GitHub Stack is read (the branch of the Stack repositories).
/// </summary>
public sealed class CatalogWorld : IEquatable<CatalogWorld>
{
    readonly string _stack;
    readonly string? _ltsName;
    // The GitHub "{owner}/{repository}" or null for a local Stack.
    readonly string? _gitHubPath;
    readonly string _toString;

    CatalogWorld( string stack, string? ltsName, string? gitHubPath )
    {
        _stack = stack;
        _ltsName = ltsName;
        _gitHubPath = gitHubPath;
        _toString = gitHubPath != null
                        ? (ltsName != null ? $"{stack}/tree/main/{ltsName}" : stack)
                        : (ltsName != null ? Path.Combine( stack, ltsName ) : stack);
    }

    /// <summary>
    /// Gets the Stack: the URL of its GitHub repository (<c>https://github.com/{owner}/{repository}</c>) or the full
    /// path of the folder of its clone.
    /// </summary>
    public string Stack => _stack;

    /// <summary>
    /// Gets the LTS name ("@net8") of a Long Term Support World, null for the default World.
    /// </summary>
    public string? LTSName => _ltsName;

    /// <summary>
    /// Gets whether the Stack is a local folder.
    /// </summary>
    public bool IsLocal => _gitHubPath == null;

    /// <summary>
    /// Gets a relative path that identifies this World among others (the files of a remote World are cached under it):
    /// "github.com/{owner}/{repository}" followed by the LTS name if any, or null for a local Stack (its files are read
    /// where they are).
    /// </summary>
    public string? CachePath => _gitHubPath == null
                                    ? null
                                    : $"github.com/{_gitHubPath}{(_ltsName != null ? "/" + _ltsName : "")}".ToLowerInvariant();

    /// <summary>
    /// Gets the location of a file of the <c>Published</c> folder of this World: the URL of its raw content for a GitHub
    /// Stack, its file path for a local one.
    /// </summary>
    /// <param name="path">The '/' separated path in the Published folder ("index.json", "explo/uc/v2026.11.23-0.uc.json").</param>
    /// <returns>The URL or the file path.</returns>
    public string GetLocation( string path )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( path );
        var lts = _ltsName != null ? _ltsName + "/" : "";
        return _gitHubPath != null
                ? $"https://raw.githubusercontent.com/{_gitHubPath}/main/{lts}Published/{path}"
                : Path.Combine( _stack, lts + "Published", path ).Replace( '/', Path.DirectorySeparatorChar );
    }

    /// <summary>
    /// Parses a World or throws an <see cref="ArgumentException"/>. See <see cref="TryParse(string?, out CatalogWorld?)"/>.
    /// </summary>
    /// <param name="world">The World.</param>
    /// <returns>The World.</returns>
    public static CatalogWorld Parse( string? world ) => TryParse( world, out var w )
                                                            ? w
                                                            : throw new ArgumentException( $"Invalid World: '{world}'. Expected a GitHub Stack URL (optionally followed by '/tree/main/@{{LTS}}') or a local folder.", nameof( world ) );

    /// <summary>
    /// Tries to parse a World: a GitHub repository URL (<c>https://github.com/{owner}/{repository}</c>, optionally
    /// followed by <c>/tree/main/@{LTS}</c> for an LTS World) or a local folder (whose last segment is the LTS name of an
    /// LTS World). A local folder must exist.
    /// </summary>
    /// <param name="world">The string to parse.</param>
    /// <param name="result">The World on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? world, [NotNullWhen( true )] out CatalogWorld? result )
    {
        result = null;
        if( string.IsNullOrWhiteSpace( world ) ) return false;
        world = world.Trim();
        if( Uri.TryCreate( world.TrimEnd( '/' ), UriKind.Absolute, out var u ) && !u.IsFile )
        {
            if( !u.Host.Equals( "github.com", StringComparison.OrdinalIgnoreCase ) ) return false;
            var segments = u.AbsolutePath.Trim( '/' ).Split( '/' );
            string? lts = null;
            if( segments.Length == 5
                && segments[2].Equals( "tree", StringComparison.Ordinal )
                && segments[3].Equals( "main", StringComparison.Ordinal )
                && WorldName.IsValidLTSName( segments[4] ) )
            {
                lts = segments[4];
            }
            else if( segments.Length != 2 )
            {
                return false;
            }
            var repository = segments[1].EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) ? segments[1][..^4] : segments[1];
            if( segments[0].Length == 0 || repository.Length == 0 ) return false;
            result = new CatalogWorld( $"https://github.com/{segments[0]}/{repository}", lts, $"{segments[0]}/{repository}" );
            return true;
        }
        string folder;
        try
        {
            folder = Path.GetFullPath( world ).TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
        }
        catch( Exception )
        {
            return false;
        }
        if( !Directory.Exists( folder ) ) return false;
        var name = Path.GetFileName( folder );
        if( WorldName.IsValidLTSName( name ) )
        {
            var parent = Path.GetDirectoryName( folder );
            if( parent == null ) return false;
            result = new CatalogWorld( parent, name, null );
        }
        else
        {
            result = new CatalogWorld( folder, null, null );
        }
        return true;
    }

    /// <summary>
    /// Gets the canonical form of this World (what <see cref="Parse(string?)"/> reads back).
    /// </summary>
    /// <returns>The World.</returns>
    public override string ToString() => _toString;

    /// <inheritdoc />
    public bool Equals( CatalogWorld? other ) => other is not null
                                                 && _toString.Equals( other._toString, StringComparison.OrdinalIgnoreCase );

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is CatalogWorld w && Equals( w );

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode( _toString );
}
