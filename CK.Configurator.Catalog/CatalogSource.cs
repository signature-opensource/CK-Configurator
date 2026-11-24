using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// The source of a catalog: a published profile (one version of each package of a World) and the NuGet feeds from
/// which its packages are restored: the feeds of the profile (<see cref="PublishedProfile.Feeds"/>) unless others are
/// given (a profile written before the profiles carried their feeds has none). See <see cref="CatalogSources"/> to
/// obtain the source of a profile of a <see cref="CatalogWorld"/>.
/// </summary>
public sealed class CatalogSource
{
    /// <summary>
    /// Initializes a new source.
    /// </summary>
    /// <param name="profile">The published profile.</param>
    /// <param name="feeds">The feeds to use instead of the ones of the profile.</param>
    public CatalogSource( PublishedProfile profile, IReadOnlyList<PublishedFeed>? feeds = null )
    {
        ArgumentNullException.ThrowIfNull( profile );
        Profile = profile;
        Feeds = feeds ?? (IReadOnlyList<PublishedFeed>)profile.Feeds;
        if( Feeds.Count == 0 )
        {
            throw new ArgumentException( $"The profile '{profile}' has no feeds (it predates them): the feeds must be given.", nameof( feeds ) );
        }
    }

    /// <summary>
    /// Gets the published profile.
    /// </summary>
    public PublishedProfile Profile { get; }

    /// <summary>
    /// Gets the feeds (a URL or a local folder each). Their secrets, when they need some, are never here: NuGet finds
    /// them where it always does (the user's NuGet configuration, the environment). The public read credentials (not
    /// secrets) are written in the nuget.config of the worker.
    /// </summary>
    public IReadOnlyList<PublishedFeed> Feeds { get; }

    /// <summary>
    /// Gets the packages of the profile that the catalog considers (see <see cref="IsCatalogPackage(string)"/>),
    /// ordered by identifier.
    /// </summary>
    public IEnumerable<PackageInstance> CatalogPackages => Profile.ProducedPackages.Values
                                                                  .Where( p => IsCatalogPackage( p.PackageId ) )
                                                                  .OrderBy( p => p.PackageId, StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Gets the CK.Configurator.Catalog.Worker package of the profile (the analyzer built with the packages of the profile),
    /// or null when the profile has none (another World).
    /// </summary>
    public PackageInstance? Worker => Profile.ProducedPackages.Values
                                             .FirstOrDefault( p => p.PackageId.Equals( "CK.Configurator.Catalog.Worker", StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Gets whether a produced package is considered by the catalog. Excluded: the test packages ("CK.Testing.*",
    /// "*.Tests"), "CK.AppBuilder" (its build files make the referencing project run the App builder after its build)
    /// and "CK.Configurator.Catalog.Worker" (the analyzer itself: the worker project already references it).
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>True when the package is considered.</returns>
    public static bool IsCatalogPackage( string packageId )
    {
        return !packageId.StartsWith( "CK.Testing.", StringComparison.OrdinalIgnoreCase )
               && !packageId.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase )
               && !packageId.Equals( "CK.AppBuilder", StringComparison.OrdinalIgnoreCase )
               && !packageId.Equals( "CK.Configurator.Catalog.Worker", StringComparison.OrdinalIgnoreCase );
    }
}
