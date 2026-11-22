using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Configurator.Catalog;

/// <summary>
/// Obtains the source of a catalog from the <c>Published</c> folder of a Stack: its <c>index.json</c> gives the latest
/// alive profile of a branch (or of its CI builds), the profile gives the packages and their feeds.
/// </summary>
public static class CatalogSources
{
    /// <summary>
    /// Gets the latest alive profile of a branch of a Stack.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="stack">
    /// The Stack: the URL of its GitHub repository (<c>https://github.com/signature-opensource/CK-Stack</c>: its
    /// <c>main</c> branch is read) or the local folder of its clone.
    /// </param>
    /// <param name="branchName">
    /// The branch: "stable" (or the empty string) for the stable line, "alpha" to "zulu", "explo/{name}".
    /// </param>
    /// <param name="isCI">True for the CI builds of the branch.</param>
    /// <param name="feeds">The feeds to use instead of the ones of the profile (a profile that predates them has none).</param>
    /// <param name="http">The HTTP client for a GitHub Stack (a new one when null).</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The source or null on error.</returns>
    public static async Task<CatalogSource?> ResolveAsync( IActivityMonitor monitor,
                                                           string stack,
                                                           string branchName,
                                                           bool isCI,
                                                           IReadOnlyList<PublishedFeed>? feeds = null,
                                                           HttpClient? http = null,
                                                           CancellationToken cancel = default )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( stack );
        ArgumentNullException.ThrowIfNull( branchName );
        if( branchName.Equals( "stable", StringComparison.OrdinalIgnoreCase ) ) branchName = "";
        var group = PublishedIndex.GetGroupName( branchName, isCI );
        using( monitor.OpenInfo( $"Resolving the latest profile of '{group}' from '{stack}'." ) )
        {
            bool ownClient = http == null;
            http ??= new HttpClient();
            try
            {
                var index = await ReadAsync( monitor, stack, "index.json", http, cancel ).ConfigureAwait( false );
                if( index == null ) return null;
                var alive = PublishedIndex.Parse( index ).GetAlive( group );
                if( alive.Length == 0 )
                {
                    monitor.Error( $"No alive profile for '{group}' in '{stack}'." );
                    return null;
                }
                var version = alive[0];
                var profileBytes = await ReadAsync( monitor, stack, PublishedProfile.GetProfilePath( version, suffix: ".json" ), http, cancel )
                                         .ConfigureAwait( false );
                if( profileBytes == null ) return null;
                var profile = PublishedProfile.Parse( profileBytes );
                if( feeds == null && profile.Feeds.Length == 0 )
                {
                    monitor.Error( $"The profile '{profile}' has no feeds (it predates them): the feeds must be given." );
                    return null;
                }
                monitor.CloseGroup( $"'{profile}' ({profile.ProducedPackages.Count} packages)." );
                return new CatalogSource( profile, feeds );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unable to resolve '{group}' from '{stack}'.", ex );
                return null;
            }
            finally
            {
                if( ownClient ) http.Dispose();
            }
        }
    }

    // Reads a file of the Published folder of the Stack ('/' separated path).
    static async Task<byte[]?> ReadAsync( IActivityMonitor monitor, string stack, string path, HttpClient http, CancellationToken cancel )
    {
        if( Directory.Exists( stack ) )
        {
            var file = Path.Combine( stack, "Published", path.Replace( '/', Path.DirectorySeparatorChar ) );
            if( !File.Exists( file ) )
            {
                monitor.Error( $"Missing file '{file}'." );
                return null;
            }
            return await File.ReadAllBytesAsync( file, cancel ).ConfigureAwait( false );
        }
        var url = GetRawUrl( stack, path );
        if( url == null )
        {
            monitor.Error( $"'{stack}' is neither a local folder nor a GitHub repository URL." );
            return null;
        }
        using var response = await http.GetAsync( url, cancel ).ConfigureAwait( false );
        if( !response.IsSuccessStatusCode )
        {
            monitor.Error( $"GET '{url}': {(int)response.StatusCode} {response.ReasonPhrase}." );
            return null;
        }
        return await response.Content.ReadAsByteArrayAsync( cancel ).ConfigureAwait( false );
    }

    /// <summary>
    /// Gets the URL of the raw content of a file of the Published folder of a Stack hosted by GitHub (its <c>main</c>
    /// branch: the branch of the Stack repositories).
    /// </summary>
    /// <param name="stackUrl">The URL of the Stack repository.</param>
    /// <param name="path">The path in the Published folder ('/' separated).</param>
    /// <returns>The URL or null when the Stack URL is not a GitHub repository URL.</returns>
    public static string? GetRawUrl( string stackUrl, string path )
    {
        if( !Uri.TryCreate( stackUrl.TrimEnd( '/' ), UriKind.Absolute, out var u )
            || !u.Host.Equals( "github.com", StringComparison.OrdinalIgnoreCase ) )
        {
            return null;
        }
        var segments = u.AbsolutePath.Trim( '/' ).Split( '/' );
        if( segments.Length != 2 ) return null;
        var repository = segments[1].EndsWith( ".git", StringComparison.OrdinalIgnoreCase ) ? segments[1][..^4] : segments[1];
        return $"https://raw.githubusercontent.com/{segments[0]}/{repository}/main/Published/{path}";
    }
}
