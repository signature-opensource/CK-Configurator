using CK.Configurator.Catalog;
using CK.Core;
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
/// The opened catalogs of the configurator, by their source (a Stack, a branch, a CI flag): the catalog of a source is
/// opened once (its packages analyzed when needed) and its worker server is started on its first request. The workers
/// are stopped when this service is disposed (with the application).
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
    /// Gets or sets the cache of the analyses (the workers of the sources are in its "sources" folder). Set by the host
    /// before the first catalog is opened.
    /// </summary>
    public CatalogCache Cache
    {
        get => _cache;
        set => _cache = value ?? throw new ArgumentNullException( nameof( value ) );
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
    /// <param name="stack">The Stack (the URL of its GitHub repository or the folder of its clone).</param>
    /// <param name="branch">The branch ("stable" or empty, "alpha"..."zulu", "explo/{name}").</param>
    /// <param name="isCI">True for the CI builds of the branch.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The opened catalog or null on error (errors are logged).</returns>
    public async Task<OpenedCatalog?> OpenAsync( IActivityMonitor monitor, string stack, string branch, bool isCI, CancellationToken cancel = default )
    {
        if( string.IsNullOrWhiteSpace( stack ) )
        {
            monitor.Error( "The Stack of a catalog is required." );
            return null;
        }
        branch ??= "";
        var key = $"{stack.Trim().TrimEnd( '/', '\\' )}|{(branch.Equals( "stable", StringComparison.OrdinalIgnoreCase ) ? "" : branch)}|{isCI}";
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
            var source = await CatalogSources.ResolveAsync( monitor, stack, branch, isCI, cancel: cancel ).ConfigureAwait( false );
            if( source == null ) return null;
            var workerFolder = Path.Combine( _cache.Root, "sources", source.Profile.Version.ToString() );
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
    }
}
