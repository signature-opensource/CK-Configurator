using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Configurator.Catalog;

/// <summary>
/// A published profile listed by the index of a World: its version (the branch and the CI build are in it), whether it
/// is deprecated (from the index) and whether it has feeds (a profile published before the profiles carried their feeds
/// has none: a catalog can't be opened on it without feeds given).
/// </summary>
/// <param name="Version">The profile version.</param>
/// <param name="IsDeprecated">Whether the profile is deprecated.</param>
/// <param name="HasFeeds">Whether the profile has its feeds (false when the profile can't be read).</param>
public sealed record CatalogProfileEntry( TimeVersion Version, bool IsDeprecated, bool HasFeeds );

/// <summary>
/// A branch of a World and its latest profile (regular or CI build).
/// </summary>
/// <param name="Name">The branch name: "stable" for the stable line, "alpha" to "zulu", "explo/{name}".</param>
/// <param name="Latest">The latest profile of the branch.</param>
public sealed record CatalogBranch( string Name, CatalogProfileEntry Latest );

/// <summary>
/// Reads the <c>Published</c> folder of a <see cref="CatalogWorld"/>: its index (read each time: it changes with every
/// publication), its profiles (read once and kept: a profile never changes, only its deprecation, which is read from
/// the index), the branches and the profiles of a branch, and the <see cref="CatalogSource"/> of a profile.
/// <para>
/// The profiles of a remote World are kept in memory and, when a cache folder is given, as files
/// (<c>{cache folder}/{CatalogWorld.CachePath}/{profile path}</c>); the ones of a local World are read where they are.
/// </para>
/// </summary>
public sealed class CatalogSources : IDisposable
{
    readonly HttpClient _http;
    readonly bool _ownClient;
    readonly string? _cacheFolder;
    readonly ConcurrentDictionary<string, PublishedProfile> _profiles;

    /// <summary>
    /// Initializes a new reader.
    /// </summary>
    /// <param name="http">The HTTP client for the GitHub Stacks (a new one, disposed with this reader, when null).</param>
    /// <param name="cacheFolder">The folder where the profiles of the remote Worlds are kept (in memory only when null).</param>
    public CatalogSources( HttpClient? http = null, string? cacheFolder = null )
    {
        _ownClient = http == null;
        _http = http ?? new HttpClient();
        _cacheFolder = cacheFolder != null ? Path.GetFullPath( cacheFolder ) : null;
        _profiles = new ConcurrentDictionary<string, PublishedProfile>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Reads the index of a World.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The index or null on error.</returns>
    public async Task<PublishedIndex?> ReadIndexAsync( IActivityMonitor monitor, CatalogWorld world, CancellationToken cancel = default )
    {
        ArgumentNullException.ThrowIfNull( world );
        var bytes = await ReadAsync( monitor, world, PublishedIndex.IndexFileName, cancel ).ConfigureAwait( false );
        if( bytes == null ) return null;
        try
        {
            return PublishedIndex.Parse( bytes );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to read the index of '{world}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Reads a profile of a World. A profile is read once: the next calls return the same instance (its
    /// <see cref="PublishedProfile.IsDeprecated"/> may be outdated: the index tells). The file of a profile published
    /// before the <see cref="TimeVersion"/> is found by its legacy name (<see cref="PublishedProfile.GetLegacyProfilePath"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World.</param>
    /// <param name="version">The profile version.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The profile or null on error.</returns>
    public async Task<PublishedProfile?> ReadProfileAsync( IActivityMonitor monitor, CatalogWorld world, TimeVersion version, CancellationToken cancel = default )
    {
        ArgumentNullException.ThrowIfNull( world );
        ArgumentNullException.ThrowIfNull( version );
        var key = $"{world}|{version}";
        if( _profiles.TryGetValue( key, out var profile ) ) return profile;
        var path = PublishedProfile.GetProfilePath( version, suffix: ".json" );
        string? cacheFile = null;
        byte[]? bytes = null;
        if( _cacheFolder != null && world.CachePath != null )
        {
            cacheFile = Path.Combine( _cacheFolder, world.CachePath, path );
            if( File.Exists( cacheFile ) ) bytes = await File.ReadAllBytesAsync( cacheFile, cancel ).ConfigureAwait( false );
        }
        bool fromCache = bytes != null;
        if( bytes == null )
        {
            // A profile published before the TimeVersion has a file named after its legacy "Year.DayOfYear.Number"
            // version (its index has been converted, not its files).
            var legacyPath = PublishedProfile.GetLegacyProfilePath( version, suffix: ".json" );
            bytes = await ReadAsync( monitor, world, path, cancel, missingIsError: legacyPath == path ).ConfigureAwait( false );
            if( bytes == null && legacyPath != path )
            {
                bytes = await ReadAsync( monitor, world, legacyPath, cancel ).ConfigureAwait( false );
            }
            if( bytes == null ) return null;
        }
        try
        {
            profile = PublishedProfile.Parse( bytes );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to read the profile '{version}' of '{world}'{(fromCache ? $" (cached in '{cacheFile}')" : "")}.", ex );
            return null;
        }
        if( !profile.Version.Equals( version ) )
        {
            monitor.Error( $"The profile file '{path}' of '{world}' is the profile '{profile.Version}'." );
            return null;
        }
        if( cacheFile != null && !fromCache )
        {
            try
            {
                Directory.CreateDirectory( Path.GetDirectoryName( cacheFile )! );
                await File.WriteAllBytesAsync( cacheFile, bytes, cancel ).ConfigureAwait( false );
            }
            catch( IOException ex )
            {
                // A concurrent read of the same profile: the file is written by the other one.
                monitor.Warn( $"Unable to cache the profile '{version}' of '{world}' in '{cacheFile}'.", ex );
            }
        }
        return _profiles.GetOrAdd( key, profile );
    }

    /// <summary>
    /// Gets the Worlds of the Stack of a World: its default World, then the LTS Worlds that the index of the default World
    /// names (<see cref="PublishedIndex.LTSWorlds"/>, empty when it was written before CKli listed them).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">A World of the Stack.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The Worlds or null when the index of the default World can't be read.</returns>
    public async Task<IReadOnlyList<CatalogWorld>?> GetWorldsAsync( IActivityMonitor monitor, CatalogWorld world, CancellationToken cancel = default )
    {
        ArgumentNullException.ThrowIfNull( world );
        var defaultWorld = world.DefaultWorld;
        var index = await ReadIndexAsync( monitor, defaultWorld, cancel ).ConfigureAwait( false );
        if( index == null ) return null;
        return [defaultWorld, .. index.LTSWorlds.Select( defaultWorld.GetWorld )];
    }

    /// <summary>
    /// Gets the branches of a World, each with its latest profile (its regular and CI builds together), the stable line
    /// first, then by name. A branch whose profiles are all deprecated is listed only when <paramref name="includeDeprecated"/>
    /// is true.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World.</param>
    /// <param name="includeDeprecated">True to consider the deprecated profiles.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The branches or null when the index can't be read (a profile that can't be read has no feeds).</returns>
    public async Task<IReadOnlyList<CatalogBranch>?> GetBranchesAsync( IActivityMonitor monitor,
                                                                      CatalogWorld world,
                                                                      bool includeDeprecated = false,
                                                                      CancellationToken cancel = default )
    {
        var index = await ReadIndexAsync( monitor, world, cancel ).ConfigureAwait( false );
        if( index == null ) return null;
        var latest = new Dictionary<string, (TimeVersion V, bool D)>( StringComparer.Ordinal );
        Collect( index.Alive, false );
        if( includeDeprecated ) Collect( index.Deprecated, true );
        var result = new List<CatalogBranch>( latest.Count );
        foreach( var (branchName, (version, isDeprecated)) in latest.OrderBy( kv => kv.Key.Length == 0 ? 0 : 1 )
                                                                    .ThenBy( kv => kv.Key, StringComparer.Ordinal ) )
        {
            var entry = await GetEntryAsync( monitor, world, version, isDeprecated, cancel ).ConfigureAwait( false );
            result.Add( new CatalogBranch( ToBranchName( branchName ), entry ) );
        }
        return result;

        void Collect( IReadOnlyDictionary<string, System.Collections.Immutable.ImmutableArray<TimeVersion>> groups, bool deprecated )
        {
            foreach( var versions in groups.Values )
            {
                if( versions.Length == 0 ) continue;
                var v = versions[0];
                if( !latest.TryGetValue( v.BranchName, out var current ) || current.V < v )
                {
                    latest[v.BranchName] = (v, deprecated);
                }
            }
        }
    }

    /// <summary>
    /// Gets the profiles of a branch of a World (its regular and CI builds together), the latest first.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World.</param>
    /// <param name="branch">The branch: "stable" (or the empty string) for the stable line, "alpha" to "zulu", "explo/{name}".</param>
    /// <param name="includeDeprecated">True to list the deprecated profiles too.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>
    /// The profiles (empty for an unknown branch) or null when the index can't be read (a profile that can't be read has
    /// no feeds).
    /// </returns>
    public async Task<IReadOnlyList<CatalogProfileEntry>?> GetProfilesAsync( IActivityMonitor monitor,
                                                                            CatalogWorld world,
                                                                            string branch,
                                                                            bool includeDeprecated = false,
                                                                            CancellationToken cancel = default )
    {
        ArgumentNullException.ThrowIfNull( branch );
        var index = await ReadIndexAsync( monitor, world, cancel ).ConfigureAwait( false );
        if( index == null ) return null;
        var branchName = ToVersionBranchName( branch );
        var versions = new List<(TimeVersion V, bool D)>();
        foreach( var isCI in new[] { false, true } )
        {
            var group = PublishedIndex.GetGroupName( branchName, isCI );
            versions.AddRange( index.GetAlive( group ).Select( v => (v, false) ) );
            if( includeDeprecated ) versions.AddRange( index.GetDeprecated( group ).Select( v => (v, true) ) );
        }
        versions.Sort( ( a, b ) => b.V.CompareTo( a.V ) );
        var result = new List<CatalogProfileEntry>( versions.Count );
        foreach( var (v, d) in versions )
        {
            result.Add( await GetEntryAsync( monitor, world, v, d, cancel ).ConfigureAwait( false ) );
        }
        return result;
    }

    /// <summary>
    /// Gets the source of a profile of a World: the profile and its feeds (or the given ones).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World.</param>
    /// <param name="version">The profile version.</param>
    /// <param name="feeds">The feeds to use instead of the ones of the profile (a profile that predates them has none).</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The source or null on error.</returns>
    public async Task<CatalogSource?> ResolveAsync( IActivityMonitor monitor,
                                                    CatalogWorld world,
                                                    TimeVersion version,
                                                    IReadOnlyList<PublishedFeed>? feeds = null,
                                                    CancellationToken cancel = default )
    {
        var profile = await ReadProfileAsync( monitor, world, version, cancel ).ConfigureAwait( false );
        if( profile == null ) return null;
        if( feeds == null && profile.Feeds.Length == 0 )
        {
            monitor.Error( $"The profile '{profile}' of '{world}' has no feeds (it predates them): the feeds must be given." );
            return null;
        }
        return new CatalogSource( profile, feeds );
    }

    /// <summary>
    /// Gets the branch name of a profile version as the commands name it: "stable" for the stable line.
    /// </summary>
    /// <param name="version">The profile version.</param>
    /// <returns>The branch name.</returns>
    public static string GetBranchName( TimeVersion version ) => ToBranchName( version.BranchName );

    static string ToBranchName( string versionBranchName ) => versionBranchName.Length == 0 ? "stable" : versionBranchName;

    static string ToVersionBranchName( string branch ) => branch.Equals( "stable", StringComparison.OrdinalIgnoreCase ) ? "" : branch;

    // A profile that can't be read (the error is logged) is listed: it can't be opened, as a profile without feeds.
    async Task<CatalogProfileEntry> GetEntryAsync( IActivityMonitor monitor, CatalogWorld world, TimeVersion version, bool isDeprecated, CancellationToken cancel )
    {
        var profile = await ReadProfileAsync( monitor, world, version, cancel ).ConfigureAwait( false );
        return new CatalogProfileEntry( version, isDeprecated, profile != null && profile.Feeds.Length > 0 );
    }

    // Reads a file of the Published folder of a World ('/' separated path). A missing file returns null, logged as an
    // error only when missingIsError is true.
    async Task<byte[]?> ReadAsync( IActivityMonitor monitor, CatalogWorld world, string path, CancellationToken cancel, bool missingIsError = true )
    {
        var location = world.GetLocation( path );
        try
        {
            if( world.IsLocal )
            {
                if( !File.Exists( location ) )
                {
                    if( missingIsError ) monitor.Error( $"Missing file '{location}'." );
                    return null;
                }
                return await File.ReadAllBytesAsync( location, cancel ).ConfigureAwait( false );
            }
            using var response = await _http.GetAsync( location, cancel ).ConfigureAwait( false );
            if( !response.IsSuccessStatusCode )
            {
                if( response.StatusCode == System.Net.HttpStatusCode.NotFound && !missingIsError ) return null;
                monitor.Error( $"GET '{location}': {(int)response.StatusCode} {response.ReasonPhrase}." );
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync( cancel ).ConfigureAwait( false );
        }
        catch( Exception ex ) when( ex is not OperationCanceledException )
        {
            monitor.Error( $"Unable to read '{location}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Disposes the HTTP client when this reader created it.
    /// </summary>
    public void Dispose()
    {
        if( _ownClient ) _http.Dispose();
    }
}
