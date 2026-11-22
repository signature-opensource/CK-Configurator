using System.Collections.Generic;

namespace CK.Configurator.Catalog;

/// <summary>
/// What a package version declares that the configurator needs: its assemblies, their role and the declarations of
/// their types. An analysis is immutable: it is computed once per package version (and per <see cref="Format"/>) and
/// kept in the <see cref="CatalogCache"/>.
/// <para>
/// It is what the package declares, nothing more: the closures (what a selection brings in) and the command families
/// depend on the other packages that are present, they are computed by the catalog.
/// </para>
/// </summary>
public sealed class PackageAnalysis
{
    /// <summary>
    /// The format of the analyses. It is part of the key of an analysis in the cache: an analysis written by an older
    /// analyzer is not read, the package is analyzed again.
    /// </summary>
    public const int Format = 2;

    /// <summary>
    /// Gets the package identifier.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Gets the package version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the assemblies of the package (its runtime assemblies). Empty for a package without assembly (a
    /// tombstone, a meta package).
    /// </summary>
    public IReadOnlyList<AssemblyAnalysis> Assemblies { get => field ?? []; init; }

    /// <summary>
    /// Returns "PackageId@Version".
    /// </summary>
    /// <returns>The package instance.</returns>
    public override string ToString() => $"{PackageId}@{Version}";
}

/// <summary>
/// An assembly of a <see cref="PackageAnalysis"/>.
/// </summary>
public sealed class AssemblyAnalysis
{
    /// <summary>
    /// Gets the simple name of the assembly.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the role of the assembly in a CKomposable application.
    /// </summary>
    public AssemblyRole Role { get; init; }

    /// <summary>
    /// Gets the engines that the assembly requires (<c>[assembly: RequiredEngine( ... )]</c>).
    /// </summary>
    public IReadOnlyList<RequiredEngine> RequiredEngines { get => field ?? []; init; }

    /// <summary>
    /// Gets the types of the assembly that declare something for the configurator. Only the types of a
    /// <see cref="AssemblyRole.Model"/> are analyzed.
    /// </summary>
    public IReadOnlyList<TypeAnalysis> Types { get => field ?? []; init; }
}

/// <summary>
/// The role of an assembly.
/// </summary>
public enum AssemblyRole
{
    /// <summary>
    /// A regular assembly: its types are not considered by the engines.
    /// </summary>
    Basic,

    /// <summary>
    /// A model (<c>[IsModel]</c>): its types are the ones the engines process.
    /// </summary>
    Model,

    /// <summary>
    /// A model definer (<c>[IsModelDefiner]</c>): the assemblies that reference it are models.
    /// </summary>
    ModelDefiner,

    /// <summary>
    /// An engine: it runs at build time, required by the models.
    /// </summary>
    Engine
}

/// <summary>
/// An engine required by an assembly.
/// </summary>
/// <param name="Name">The name of the engine (an assembly name, the package of the same name).</param>
/// <param name="VersionRange">The optional version range.</param>
public sealed record RequiredEngine( string Name, string? VersionRange );
