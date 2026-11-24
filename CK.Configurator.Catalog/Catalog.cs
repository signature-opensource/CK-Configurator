using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// The catalog of a source: the analyses of its packages, indexed in memory (a catalog is a few hundred packages).
/// </summary>
public sealed class Catalog
{
    readonly IReadOnlyList<PackageAnalysis> _packages;
    readonly Dictionary<string, (PackageAnalysis Package, AssemblyAnalysis Assembly, TypeAnalysis Type)> _types;
    readonly Dictionary<string, List<(PackageAnalysis Package, TypeAnalysis Type)>> _configurables;
    readonly Dictionary<string, CommandFamily> _commandsByInterface;
    readonly Dictionary<string, CommandFamily> _commandsByName;

    Catalog( IActivityMonitor monitor, CatalogSource source, IReadOnlyList<PackageAnalysis> packages )
    {
        Source = source;
        _packages = packages;
        _types = new Dictionary<string, (PackageAnalysis, AssemblyAnalysis, TypeAnalysis)>( StringComparer.Ordinal );
        _configurables = new Dictionary<string, List<(PackageAnalysis, TypeAnalysis)>>( StringComparer.OrdinalIgnoreCase );
        foreach( var p in packages )
        {
            foreach( var a in p.Assemblies )
            {
                foreach( var t in a.Types )
                {
                    _types.TryAdd( t.FullName, (p, a, t) );
                    if( t.Configurable != null )
                    {
                        if( !_configurables.TryGetValue( t.Configurable.Name, out var list ) )
                        {
                            _configurables.Add( t.Configurable.Name, list = new List<(PackageAnalysis, TypeAnalysis)>() );
                        }
                        list.Add( (p, t) );
                    }
                }
            }
        }
        _commandsByInterface = CommandFamily.Compute( monitor, _types );
        _commandsByName = new Dictionary<string, CommandFamily>( StringComparer.Ordinal );
        foreach( var f in _commandsByInterface.Values.Distinct() )
        {
            if( !_commandsByName.TryAdd( f.Name, f ) )
            {
                monitor.Warn( $"Two commands are named '{f.Name}': '{_commandsByName[f.Name].Primary.Type.CSharpName}' and '{f.Primary.Type.CSharpName}'." );
            }
        }
    }

    /// <summary>
    /// Creates a catalog from analyses (tests: the source can be null).
    /// </summary>
    internal static Catalog Create( IActivityMonitor monitor, CatalogSource source, IReadOnlyList<PackageAnalysis> packages ) => new( monitor, source, packages );

    /// <summary>
    /// Loads the catalog of a source from the cache. Every package of the source must have been analyzed
    /// (see <see cref="CatalogWorker.AnalyzeAsync"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="source">The source.</param>
    /// <param name="cache">The cache.</param>
    /// <returns>The catalog or null when a package is not analyzed.</returns>
    public static Catalog? Load( IActivityMonitor monitor, CatalogSource source, CatalogCache cache )
    {
        var packages = new List<PackageAnalysis>();
        bool success = true;
        foreach( var p in source.CatalogPackages )
        {
            var a = cache.TryLoad( monitor, p.PackageId, p.Version.ToString() );
            if( a == null )
            {
                monitor.Error( $"Package '{p}' has not been analyzed." );
                success = false;
            }
            else
            {
                packages.Add( a );
            }
        }
        return success ? new Catalog( monitor, source, packages ) : null;
    }

    /// <summary>
    /// Gets the source of this catalog.
    /// </summary>
    public CatalogSource Source { get; }

    /// <summary>
    /// Gets the analyses of the packages, ordered by package identifier.
    /// </summary>
    public IReadOnlyList<PackageAnalysis> Packages => _packages;

    /// <summary>
    /// Gets the configuration names of the configurable types.
    /// </summary>
    public IEnumerable<string> ConfigurableNames => _configurables.Keys;

    /// <summary>
    /// Gets the commands, by name.
    /// </summary>
    public IReadOnlyDictionary<string, CommandFamily> Commands => _commandsByName;

    /// <summary>
    /// Finds the command of one of its interfaces.
    /// </summary>
    /// <param name="interfaceFullName">The full name of a command interface (the primary one or an extension).</param>
    /// <returns>The command or null.</returns>
    public CommandFamily? FindCommandByInterface( string interfaceFullName ) => _commandsByInterface.GetValueOrDefault( interfaceFullName );

    /// <summary>
    /// Finds a type by its full name.
    /// </summary>
    /// <param name="fullName">The full name of the type.</param>
    /// <returns>The type, its assembly and its package, or null.</returns>
    public (PackageAnalysis Package, AssemblyAnalysis Assembly, TypeAnalysis Type)? FindType( string fullName )
    {
        return _types.TryGetValue( fullName, out var t ) ? t : null;
    }

    /// <summary>
    /// Finds the configurable types of a configuration name (case insensitive). More than one is a conflict that the
    /// configuration of an application must not reach (two packages that offer the same name are not used together).
    /// </summary>
    /// <param name="name">The configuration name.</param>
    /// <returns>The configurable types (empty when none).</returns>
    public IReadOnlyList<(PackageAnalysis Package, TypeAnalysis Type)> FindConfigurable( string name )
    {
        return _configurables.TryGetValue( name, out var list ) ? list : [];
    }
}
