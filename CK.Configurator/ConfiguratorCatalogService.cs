using CK.Configurator.Catalog;
using CK.Core;
using CK.Packaging.Abstractions;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// The opened catalogs of the configurator, by their source (a World and a profile version): the catalog of a source is
/// opened once (its packages analyzed when needed) and its worker server is started on its first request. The workers
/// are stopped when this service is disposed (with the application). The branches and the profiles of a World are
/// listed by <see cref="GetBranchesAsync"/> and <see cref="GetProfilesAsync"/> (the profiles read are kept in the
/// "profiles" folder of the <see cref="Cache"/>).
/// <para>
/// Its "Configurator" section is read at start: <c>"Configurator": { "CacheRoot": "...", "AnalyzerProject": "..." }</c>
/// (see <see cref="CacheRoot"/> and <see cref="AnalyzerProject"/>).
/// </para>
/// </summary>
[ConfigurationSection( "Configurator" )]
public sealed class ConfiguratorCatalogService : ISingletonAutoService, IAsyncDisposable
{
    readonly SemaphoreSlim _gate;
    readonly Dictionary<string, OpenedCatalog> _catalogs;
    CatalogCache _cache;
    CatalogSources _sources;
    AnalyzerReference? _analyzer;

    /// <summary>
    /// Initializes the service from its "Configurator" section.
    /// </summary>
    /// <param name="configuration">The configuration of the application.</param>
    public ConfiguratorCatalogService( IConfiguration configuration )
    {
        _gate = new SemaphoreSlim( 1, 1 );
        _catalogs = new Dictionary<string, OpenedCatalog>( StringComparer.OrdinalIgnoreCase );
        var section = configuration.GetSection( "Configurator" );
        CacheRoot = section["CacheRoot"];
        AnalyzerProject = section["AnalyzerProject"];
        _cache = new CatalogCache( CacheRoot ?? CatalogCache.DefaultRoot );
        _sources = CreateSources( _cache );
        if( AnalyzerProject != null )
        {
            // Relative to the solution of the development (the solution of CK-Configurator).
            var path = Path.IsPathRooted( AnalyzerProject ) || LocalDevSolution.SolutionFolder.IsEmptyPath
                        ? AnalyzerProject
                        : Path.Combine( LocalDevSolution.SolutionFolder, AnalyzerProject );
            _analyzer = AnalyzerReference.FromProject( path );
        }
    }

    /// <summary>
    /// Gets the configured root of the cache of the analyses ("Configurator:CacheRoot"). When null,
    /// <see cref="CatalogCache.DefaultRoot"/> is used.
    /// </summary>
    public string? CacheRoot { get; }

    /// <summary>
    /// Gets the configured analyzer project ("Configurator:AnalyzerProject": the path of the CK.Configurator.Catalog.Worker
    /// project, absolute or relative to the folder of the development solution). A development build of the configurator
    /// (the zero version: there is no worker package of its version) sets it.
    /// </summary>
    public string? AnalyzerProject { get; }

    /// <summary>
    /// Gets or sets the cache of the analyses (the workers of the sources are in its "sources" folder, the profiles read
    /// in its "profiles" folder). Set by the host before the first catalog is opened.
    /// </summary>
    public CatalogCache Cache
    {
        get => _cache;
        set
        {
            ArgumentNullException.ThrowIfNull( value );
            _sources.Dispose();
            _sources = CreateSources( value );
            _cache = value;
        }
    }

    static CatalogSources CreateSources( CatalogCache cache ) => new CatalogSources( cacheFolder: Path.Combine( cache.Root, "profiles" ) );

    /// <summary>
    /// Gets the branches of a World, each with its latest profile.
    /// See <see cref="CatalogSources.GetBranchesAsync(IActivityMonitor, CatalogWorld, bool, CancellationToken)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World (see <see cref="CatalogWorld.TryParse(string?, out CatalogWorld?)"/>).</param>
    /// <param name="includeDeprecated">True to consider the deprecated profiles.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The branches or null on error (errors are logged).</returns>
    public Task<IReadOnlyList<CatalogBranch>?> GetBranchesAsync( IActivityMonitor monitor, string world, bool includeDeprecated, CancellationToken cancel = default )
    {
        var w = ParseWorld( monitor, world );
        return w == null
                ? Task.FromResult<IReadOnlyList<CatalogBranch>?>( null )
                : _sources.GetBranchesAsync( monitor, w, includeDeprecated, cancel );
    }

    /// <summary>
    /// Gets the profiles of a branch of a World, the latest first.
    /// See <see cref="CatalogSources.GetProfilesAsync(IActivityMonitor, CatalogWorld, string, bool, CancellationToken)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World (see <see cref="CatalogWorld.TryParse(string?, out CatalogWorld?)"/>).</param>
    /// <param name="branch">The branch ("stable" or empty, "alpha"..."zulu", "explo/{name}").</param>
    /// <param name="includeDeprecated">True to list the deprecated profiles too.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The profiles or null on error (errors are logged).</returns>
    public Task<IReadOnlyList<CatalogProfileEntry>?> GetProfilesAsync( IActivityMonitor monitor,
                                                                      string world,
                                                                      string branch,
                                                                      bool includeDeprecated,
                                                                      CancellationToken cancel = default )
    {
        var w = ParseWorld( monitor, world );
        return w == null
                ? Task.FromResult<IReadOnlyList<CatalogProfileEntry>?>( null )
                : _sources.GetProfilesAsync( monitor, w, branch ?? "", includeDeprecated, cancel );
    }

    static CatalogWorld? ParseWorld( IActivityMonitor monitor, string? world )
    {
        if( CatalogWorld.TryParse( world, out var w ) ) return w;
        monitor.Error( $"Invalid World '{world}': a GitHub Stack URL (optionally followed by '/tree/main/@{{LTS}}') or an existing local folder is expected." );
        return null;
    }

    /// <summary>
    /// Gets or sets how the workers reference the analyzer. Defaults to the CK.Configurator.Catalog.Worker package of
    /// the version of CK.Configurator.Catalog: a development build (the zero version) must set it (to the worker
    /// project, see <see cref="AnalyzerReference.FromProject(string)"/>).
    /// </summary>
    public AnalyzerReference? Analyzer
    {
        get => _analyzer ??= GetDefaultAnalyzer();
        set => _analyzer = value;
    }

    static AnalyzerReference? GetDefaultAnalyzer()
    {
        // The informational version is "Version/Sha/Date": the version of the package.
        var info = typeof( CatalogCache ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = info?.Split( '/' )[0];
        return string.IsNullOrEmpty( version ) || version.StartsWith( "0.0.0-0", StringComparison.Ordinal )
                ? null
                : AnalyzerReference.FromPackage( version );
    }

    /// <summary>
    /// An opened catalog.
    /// </summary>
    public sealed class OpenedCatalog
    {
        readonly ConfiguratorCatalogService _service;
        readonly string _workerFolder;
        readonly AnalyzerReference _analyzer;
        CatalogWorkerServer? _server;

        internal OpenedCatalog( ConfiguratorCatalogService service, CatalogModel catalog, string workerFolder, AnalyzerReference analyzer )
        {
            _service = service;
            Catalog = catalog;
            _workerFolder = workerFolder;
            _analyzer = analyzer;
        }

        /// <summary>
        /// Gets the catalog.
        /// </summary>
        public CatalogModel Catalog { get; }

        /// <summary>
        /// Gets the worker server of the catalog (started on the first call, restarted if it has exited).
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="cancel">Optional cancellation token.</param>
        /// <returns>The server or null on error.</returns>
        public async Task<CatalogWorkerServer?> GetServerAsync( IActivityMonitor monitor, CancellationToken cancel = default )
        {
            await _service._gate.WaitAsync( cancel ).ConfigureAwait( false );
            try
            {
                if( _server != null && !_server.IsRunning )
                {
                    await _server.DisposeAsync().ConfigureAwait( false );
                    _server = null;
                }
                return _server ??= await CatalogWorker.StartServerAsync( monitor, Catalog.Source, _workerFolder, _analyzer, cancel )
                                                      .ConfigureAwait( false );
            }
            finally
            {
                _service._gate.Release();
            }
        }

        internal ValueTask DisposeAsync() => _server?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>
    /// Opens (or gets) the catalog of a source.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="world">The World (see <see cref="CatalogWorld.TryParse(string?, out CatalogWorld?)"/>).</param>
    /// <param name="version">The version of the profile.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The opened catalog or null on error (errors are logged).</returns>
    public async Task<OpenedCatalog?> OpenAsync( IActivityMonitor monitor, string world, string version, CancellationToken cancel = default )
    {
        var w = ParseWorld( monitor, world );
        if( w == null ) return null;
        if( !TimeVersion.TryParse( version, out var v ) )
        {
            monitor.Error( $"Invalid profile version '{version}'." );
            return null;
        }
        var key = $"{w}|{v}";
        await _gate.WaitAsync( cancel ).ConfigureAwait( false );
        try
        {
            if( _catalogs.TryGetValue( key, out var opened ) ) return opened;
            var analyzer = Analyzer;
            if( analyzer == null )
            {
                monitor.Error( "The analyzer of the workers is unknown (a development build): ConfiguratorCatalogService.Analyzer must be set." );
                return null;
            }
            var source = await _sources.ResolveAsync( monitor, w, v, cancel: cancel ).ConfigureAwait( false );
            if( source == null ) return null;
            var workerFolder = Path.Combine( _cache.Root, "sources", source.Profile.World.FullName, source.Profile.Version.ToString() );
            if( !await CatalogWorker.AnalyzeAsync( monitor, source, _cache, workerFolder, analyzer, cancel ).ConfigureAwait( false ) ) return null;
            var catalog = CatalogModel.Load( monitor, source, _cache );
            if( catalog == null ) return null;
            opened = new OpenedCatalog( this, catalog, workerFolder, analyzer );
            _catalogs.Add( key, opened );
            return opened;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops the workers of the opened catalogs.
    /// </summary>
    /// <returns>The awaitable.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach( var c in _catalogs.Values ) await c.DisposeAsync().ConfigureAwait( false );
        _catalogs.Clear();
        _sources.Dispose();
    }
}
