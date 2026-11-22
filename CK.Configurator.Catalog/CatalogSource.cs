using CK.Core;
using CK.Packaging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// The source of a catalog: a published profile (one version of each package of a World) and the NuGet feeds from
/// which its packages are restored.
/// <para>
/// The feeds will come from the profile itself once CKli writes them (a Stack URL, a branch and a CI flag will then be
/// enough to obtain a source); until then, they are given.
/// </para>
/// </summary>
public sealed class CatalogSource
{
    /// <summary>
    /// Initializes a new source.
    /// </summary>
    /// <param name="profile">The published profile.</param>
    /// <param name="feeds">The feeds (a URL or a local folder each).</param>
    public CatalogSource( PublishedProfile profile, IReadOnlyList<CatalogFeed> feeds )
    {
        ArgumentNullException.ThrowIfNull( profile );
        ArgumentNullException.ThrowIfNull( feeds );
        Profile = profile;
        Feeds = feeds;
    }

    /// <summary>
    /// Gets the published profile.
    /// </summary>
    public PublishedProfile Profile { get; }

    /// <summary>
    /// Gets the feeds.
    /// </summary>
    public IReadOnlyList<CatalogFeed> Feeds { get; }

    /// <summary>
    /// Gets the packages of the profile that the catalog considers (see <see cref="IsCatalogPackage(string)"/>),
    /// ordered by identifier.
    /// </summary>
    public IEnumerable<PackageInstance> CatalogPackages => Profile.ProducedPackages.Values
                                                                  .Where( p => IsCatalogPackage( p.PackageId ) )
                                                                  .OrderBy( p => p.PackageId, StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Gets whether a produced package is considered by the catalog. Excluded: the test packages ("CK.Testing.*",
    /// "*.Tests") and "CK.AppBuilder" (its build files make the referencing project run the App builder after its build).
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>True when the package is considered.</returns>
    public static bool IsCatalogPackage( string packageId )
    {
        return !packageId.StartsWith( "CK.Testing.", StringComparison.OrdinalIgnoreCase )
               && !packageId.EndsWith( ".Tests", StringComparison.OrdinalIgnoreCase )
               && !packageId.Equals( "CK.AppBuilder", StringComparison.OrdinalIgnoreCase );
    }
}

/// <summary>
/// A NuGet feed of a <see cref="CatalogSource"/>. Its credentials, when it needs some, are found where NuGet finds them
/// (the user's NuGet configuration, the environment): never here.
/// </summary>
/// <param name="Name">The feed name (the key of the source in the generated nuget.config).</param>
/// <param name="Url">The feed URL or a local folder.</param>
public sealed record CatalogFeed( string Name, string Url );
