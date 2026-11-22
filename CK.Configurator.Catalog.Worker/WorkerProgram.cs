using CK.Core;
using CK.Setup;
using CK.TypeCache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CK.Configurator.Catalog.Worker;

/// <summary>
/// The program of a worker (see <see cref="CatalogWorker"/>): its base directory holds the packages of one source, it
/// analyzes the requested ones and saves their analyses in the cache.
/// </summary>
public static class WorkerProgram
{
    /// <summary>
    /// Runs the worker.
    /// </summary>
    /// <param name="args">The arguments (see <see cref="CatalogWorker.Arguments"/>).</param>
    /// <returns>The exit code: 0 on success.</returns>
    public static int Run( string[] args )
    {
        bool serve = Array.IndexOf( args, CatalogWorker.Arguments.Serve ) >= 0;
        var monitor = new ActivityMonitor( "CK.Configurator.Catalog.Worker" );
        // A server answers on its standard output: its logs go to its standard error.
        monitor.Output.RegisterClient( serve
                                        ? new ActivityMonitorTextWriterClient( Console.Error.Write )
                                        : new ActivityMonitorConsoleClient() );
        try
        {
            var assets = GetArgument( args, CatalogWorker.Arguments.Assets );
            if( serve )
            {
                if( assets == null )
                {
                    monitor.Error( $"Usage: {CatalogWorker.Arguments.Serve} {CatalogWorker.Arguments.Assets} <project.assets.json>" );
                    return 1;
                }
                return WorkerServer.Run( monitor, ReadPackageAssemblies( assets ) ) ? 0 : 2;
            }
            var cacheRoot = GetArgument( args, CatalogWorker.Arguments.Cache );
            var packagesFile = GetArgument( args, CatalogWorker.Arguments.Packages );
            if( assets == null || cacheRoot == null || packagesFile == null )
            {
                monitor.Error( $"Usage: {CatalogWorker.Arguments.Assets} <project.assets.json> {CatalogWorker.Arguments.Cache} <folder> {CatalogWorker.Arguments.Packages} <file>" );
                return 1;
            }
            var requested = File.ReadAllLines( packagesFile )
                                .Select( l => l.Trim() )
                                .Where( l => l.Length > 0 )
                                .Select( l => { int i = l.LastIndexOf( '@' ); return (Id: l[..i], Version: l[(i + 1)..]); } )
                                .ToList();
            var packageAssemblies = ReadPackageAssemblies( assets );
            return Analyze( monitor, new CatalogCache( cacheRoot ), requested, packageAssemblies ) ? 0 : 2;
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            return 3;
        }
        finally
        {
            monitor.MonitorEnd();
        }
    }

    static string? GetArgument( string[] args, string name )
    {
        int i = Array.IndexOf( args, name );
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Reads the runtime assemblies of each package ("PackageId/Version" lower case) from a project.assets.json.
    /// </summary>
    /// <param name="assetsPath">The project.assets.json path.</param>
    /// <returns>The simple assembly names of each package.</returns>
    public static Dictionary<string, List<string>> ReadPackageAssemblies( string assetsPath )
    {
        var result = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
        using var doc = JsonDocument.Parse( File.ReadAllBytes( assetsPath ) );
        foreach( var target in doc.RootElement.GetProperty( "targets" ).EnumerateObject() )
        {
            // The worker has one target (net10.0, no runtime identifier).
            foreach( var lib in target.Value.EnumerateObject() )
            {
                if( lib.Value.GetProperty( "type" ).GetString() != "package" ) continue;
                var names = new List<string>();
                if( lib.Value.TryGetProperty( "runtime", out var runtime ) )
                {
                    foreach( var f in runtime.EnumerateObject() )
                    {
                        if( f.Name.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) )
                        {
                            names.Add( Path.GetFileNameWithoutExtension( f.Name ) );
                        }
                    }
                }
                result[lib.Name] = names;
            }
            break;
        }
        return result;
    }

    /// <summary>
    /// Loads the assemblies of the worker's folder (the discover mode of the assembly cache on its base directory).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The assembly cache result or null on error.</returns>
    internal static AssemblyCache.Result? LoadTypes( IActivityMonitor monitor )
    {
        var config = new EngineConfiguration();
        config.FirstBinPath.Path = AppContext.BaseDirectory;
        config.FirstBinPath.DiscoverAssembliesFromPath = true;
        var result = AssemblyCache.Run( monitor, config );
        if( !result.Success )
        {
            monitor.Error( "The assembly cache failed on the worker's folder." );
            return null;
        }
        return result;
    }

    static bool Analyze( IActivityMonitor monitor,
                         CatalogCache cache,
                         List<(string Id, string Version)> requested,
                         Dictionary<string, List<string>> packageAssemblies )
    {
        var result = LoadTypes( monitor );
        if( result == null ) return false;
        var analyzer = new TypeAnalyzer( monitor, result.TypeCache );
        bool success = true;
        foreach( var (id, version) in requested )
        {
            if( !packageAssemblies.TryGetValue( $"{id}/{version}", out var names ) )
            {
                monitor.Error( $"Package '{id}@{version}' is not in the worker's assets." );
                success = false;
                continue;
            }
            using( monitor.OpenInfo( $"Analyzing '{id}@{version}'." ) )
            {
                var assemblies = new List<AssemblyAnalysis>();
                foreach( var name in names )
                {
                    if( !result.AssemblyCache.Assemblies.TryGetValue( name, out var cached ) )
                    {
                        monitor.Warn( $"Assembly '{name}' of '{id}' has not been loaded by the assembly cache." );
                        continue;
                    }
                    assemblies.Add( analyzer.Analyze( cached ) );
                }
                cache.Save( new PackageAnalysis { PackageId = id, Version = version, Assemblies = assemblies } );
            }
        }
        return success && analyzer.Success;
    }
}
