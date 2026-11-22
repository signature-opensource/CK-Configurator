using CK.Core;
using System;
using System.IO;

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
