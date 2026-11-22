using CK.Core;
using CK.TypeCache;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Configurator.Catalog.Worker;

/// <summary>
/// The server mode of a worker: the packages are loaded once, the <see cref="WorkerRequest"/> lines of the standard
/// input are answered on the standard output until the input is closed.
/// </summary>
static class WorkerServer
{
    public static bool Run( IActivityMonitor monitor, Dictionary<string, List<string>> packageAssemblies )
    {
        var result = WorkerProgram.LoadTypes( monitor );
        if( result == null ) return false;
        // The package identifier (without its version) to its assemblies.
        var byPackage = new Dictionary<string, List<string>>( StringComparer.OrdinalIgnoreCase );
        foreach( var (key, names) in packageAssemblies )
        {
            byPackage[key[..key.IndexOf( '/' )]] = names;
        }
        monitor.Info( "Ready." );
        string? line;
        while( (line = Console.In.ReadLine()) != null )
        {
            if( line.Length == 0 ) continue;
            WorkerResponse response;
            int id = 0;
            using( monitor.CollectEntries( out var errors, LogLevelFilter.Error ) )
            {
                ClosureResult? closure = null;
                try
                {
                    var request = WorkerProtocol.ReadRequest( line );
                    id = request.Id;
                    if( request.Closure != null )
                    {
                        closure = ComputeClosure( monitor, result, byPackage, request.Closure );
                    }
                    else
                    {
                        monitor.Error( "Empty request." );
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( "Request failed.", ex );
                }
                response = errors.Count > 0
                            ? new WorkerResponse { Id = id, Errors = errors.Select( e => e.Text ).ToList() }
                            : new WorkerResponse { Id = id, Closure = closure };
            }
            Console.Out.WriteLine( WorkerProtocol.Write( response ) );
            Console.Out.Flush();
        }
        monitor.Info( "Input closed: stopping." );
        return true;
    }

    static ClosureResult? ComputeClosure( IActivityMonitor monitor,
                                          AssemblyCache.Result result,
                                          Dictionary<string, List<string>> byPackage,
                                          ClosureRequest request )
    {
        // The candidates: the concrete resource groups of the models of the packages (an abstraction is never a package).
        var candidates = new List<ICachedType>();
        foreach( var p in request.Packages )
        {
            if( !byPackage.TryGetValue( p, out var names ) )
            {
                monitor.Error( $"Package '{p}' is not in the worker." );
                continue;
            }
            foreach( var name in names )
            {
                if( !result.AssemblyCache.Assemblies.TryGetValue( name, out var a ) || !a.Kind.IsModel() ) continue;
                foreach( var t in TypeAnalyzer.GetModelTypes( a, result.TypeCache ) )
                {
                    if( !typeof( IResourceGroup ).IsAssignableFrom( t.Type ) || t.Type.IsGenericTypeDefinition ) continue;
                    if( t.TryGetMapping( monitor, out var mapping ) && !mapping.IsKey ) candidates.Add( t );
                }
            }
        }
        var roots = new HashSet<string>( request.Roots, StringComparer.Ordinal );
        foreach( var unknown in roots.Except( candidates.Select( c => c.Type.FullName! ), StringComparer.Ordinal ) )
        {
            monitor.Error( $"Root '{unknown}' is not a package of the requested packages." );
        }
        var kept = ResPackageClosure.Compute( monitor, result.TypeCache, candidates, c => roots.Contains( c.Type.FullName! ) );
        return kept == null
                ? null
                : new ClosureResult { Kept = kept.Select( k => k.Type.FullName! ).Order( StringComparer.Ordinal ).ToList() };
    }
}
