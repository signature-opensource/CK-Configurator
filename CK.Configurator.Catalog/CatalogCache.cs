using CK.Core;
using System;
using System.IO;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// The flat file store of the analyses: one immutable file per package version and per <see cref="PackageAnalysis.Format"/>
/// (<c>packages/&lt;package id&gt;/&lt;version&gt;.v&lt;format&gt;.json</c>, lower case), shared by all the catalogs
/// (the analysis of a package version doesn't depend on the source that lists it).
/// </summary>
public sealed class CatalogCache
{
    readonly string _root;

    /// <summary>
    /// Initializes a cache on a root folder (created when needed).
    /// </summary>
    /// <param name="root">The root folder.</param>
    public CatalogCache( string root )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( root );
        _root = Path.GetFullPath( root );
    }

    /// <summary>
    /// Gets the default root: "CK-Configurator/Catalog" in the local application data folder of the user.
    /// </summary>
    public static string DefaultRoot => Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
                                                      "CK-Configurator",
                                                      "Catalog" );

    /// <summary>
    /// Gets the root folder.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Gets the path of the analysis of a package version.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The file path.</returns>
    public string GetAnalysisPath( string packageId, string version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( packageId );
        ArgumentException.ThrowIfNullOrWhiteSpace( version );
        return Path.Combine( _root,
                             "packages",
                             packageId.ToLowerInvariant(),
                             $"{version.ToLowerInvariant()}.v{PackageAnalysis.Format}.json" );
    }

    /// <summary>
    /// Gets whether an analysis of a package version exists in another <see cref="PackageAnalysis.Format"/> (written by
    /// another analyzer).
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <returns>True if an analysis of another format is in the cache.</returns>
    public bool HasOtherFormat( string packageId, string version )
    {
        var mine = GetAnalysisPath( packageId, version );
        var folder = Path.GetDirectoryName( mine )!;
        return Directory.Exists( folder )
               && Directory.EnumerateFiles( folder, $"{version.ToLowerInvariant()}.v*.json" ).Any( f => !f.Equals( mine, StringComparison.OrdinalIgnoreCase ) );
    }

    /// <summary>
    /// Gets or sets whether a version of the worker package writes another <see cref="PackageAnalysis.Format"/> (a marker
    /// file in the cache): it is not tried again.
    /// </summary>
    /// <param name="workerVersion">The version of the CK.Configurator.Catalog.Worker package.</param>
    /// <returns>True when the worker is known to write another format.</returns>
    public bool IsIncompatibleWorker( string workerVersion ) => File.Exists( GetIncompatibleWorkerPath( workerVersion ) );

    /// <summary>
    /// Marks a version of the worker package as writing another <see cref="PackageAnalysis.Format"/>.
    /// </summary>
    /// <param name="workerVersion">The version of the CK.Configurator.Catalog.Worker package.</param>
    public void SetIncompatibleWorker( string workerVersion )
    {
        var path = GetIncompatibleWorkerPath( workerVersion );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, $"This worker doesn't write the analyses of format {PackageAnalysis.Format}." );
    }

    string GetIncompatibleWorkerPath( string workerVersion )
        => Path.Combine( _root, "workers", $"{workerVersion.ToLowerInvariant()}.v{PackageAnalysis.Format}.incompatible" );

    /// <summary>
    /// Gets whether the analysis of a package version exists.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <returns>True if the analysis is in the cache.</returns>
    public bool Contains( string packageId, string version ) => File.Exists( GetAnalysisPath( packageId, version ) );

    /// <summary>
    /// Loads the analysis of a package version. An unreadable file is logged as a warning and deleted: the package
    /// will be analyzed again.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The package version.</param>
    /// <returns>The analysis or null if it is not in the cache.</returns>
    public PackageAnalysis? TryLoad( IActivityMonitor monitor, string packageId, string version )
    {
        var path = GetAnalysisPath( packageId, version );
        if( !File.Exists( path ) ) return null;
        try
        {
            return CatalogJson.Read( File.ReadAllBytes( path ) );
        }
        catch( Exception ex )
        {
            monitor.Warn( $"Unreadable analysis '{path}': it is deleted.", ex );
            File.Delete( path );
            return null;
        }
    }

    /// <summary>
    /// Saves an analysis. The file is written next to its final path and moved: a reader never sees a partial file.
    /// </summary>
    /// <param name="analysis">The analysis to save.</param>
    public void Save( PackageAnalysis analysis )
    {
        ArgumentNullException.ThrowIfNull( analysis );
        var path = GetAnalysisPath( analysis.PackageId, analysis.Version );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes( temp, CatalogJson.Write( analysis ) );
        File.Move( temp, path, overwrite: true );
    }
}
