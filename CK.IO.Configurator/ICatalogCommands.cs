using CK.Core;
using CK.Cris;
using System.Collections.Generic;

namespace CK.IO.Configurator;

/// <summary>
/// The World that a command targets: a Stack and, for a Long Term Support World, its LTS name.
/// </summary>
public interface ICommandCatalogWorld : ICommandPart
{
    /// <summary>
    /// Gets or sets the World: the URL of the GitHub repository of its Stack ("https://github.com/signature-opensource/CK-Stack"
    /// is its default World, ".../CK-Stack/tree/main/@net8" its "@net8" LTS World) or the local folder of the clone of
    /// the Stack (its "@net8" sub folder for the LTS World).
    /// </summary>
    string World { get; set; }
}

/// <summary>
/// The catalog that a command targets: a published profile of a World (its version names its branch and its CI build).
/// The backend keeps the opened catalogs by their source.
/// </summary>
[CKTypeDefiner]
public interface ICommandCatalogSource : ICommandCatalogWorld
{
    /// <summary>
    /// Gets or sets the version of the profile ("2026.11.23-0.uc.0.ci.0").
    /// </summary>
    string Version { get; set; }
}

/// <summary>
/// Lists the branches of a World, each with its latest profile (its regular and CI builds together): the stable line
/// first, then by name.
/// </summary>
public interface IGetBranchesQCommand : ICommand<IList<IBranchInfo>>, ICommandCatalogWorld
{
    /// <summary>
    /// Gets or sets whether the deprecated profiles are considered (a branch whose profiles are all deprecated is
    /// listed only then).
    /// </summary>
    bool IncludeDeprecated { get; set; }
}

/// <summary>
/// A branch of a World.
/// </summary>
public interface IBranchInfo : IPoco
{
    /// <summary>
    /// Gets or sets the branch name: "stable" for the stable line, "alpha" to "zulu", "explo/{name}".
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets its latest profile.
    /// </summary>
    IProfileInfo Latest { get; }
}

/// <summary>
/// Lists the profiles of a branch of a World (its regular and CI builds together), the latest first.
/// </summary>
public interface IGetProfilesQCommand : ICommand<IList<IProfileInfo>>, ICommandCatalogWorld
{
    /// <summary>
    /// Gets or sets the branch: "stable" (or empty) for the stable line, "alpha" to "zulu", "explo/{name}".
    /// </summary>
    string Branch { get; set; }

    /// <summary>
    /// Gets or sets whether the deprecated profiles are listed.
    /// </summary>
    bool IncludeDeprecated { get; set; }
}

/// <summary>
/// A published profile of a World.
/// </summary>
public interface IProfileInfo : IPoco
{
    /// <summary>
    /// Gets or sets the version of the profile (the <see cref="ICommandCatalogSource.Version"/> of a catalog).
    /// </summary>
    string Version { get; set; }

    /// <summary>
    /// Gets or sets the day of the publication ("2026.11.23").
    /// </summary>
    string Date { get; set; }

    /// <summary>
    /// Gets or sets the number that tells apart the publications of the same day (0 for the first one).
    /// </summary>
    int ClashNumber { get; set; }

    /// <summary>
    /// Gets or sets whether the profile is a CI build.
    /// </summary>
    bool IsCI { get; set; }

    /// <summary>
    /// Gets or sets whether the profile is deprecated.
    /// </summary>
    bool IsDeprecated { get; set; }

    /// <summary>
    /// Gets or sets whether the profile has its feeds: a profile published before the profiles carried their feeds has
    /// none (nor has a profile that can't be read), a catalog can't be opened on it.
    /// </summary>
    bool HasFeeds { get; set; }
}

/// <summary>
/// Opens a catalog: its source is resolved, the packages that are not analyzed yet are analyzed (about a minute when
/// nothing is: the packages are restored and their types are loaded). Returns when the catalog is ready.
/// </summary>
public interface IOpenCatalogCommand : ICommand<ICatalogInfo>, ICommandCatalogSource
{
}

/// <summary>
/// What an opened catalog is.
/// </summary>
public interface ICatalogInfo : IPoco
{
    /// <summary>
    /// Gets or sets the number of packages of the catalog.
    /// </summary>
    int PackageCount { get; set; }

    /// <summary>
    /// Gets or sets the number of configurable types.
    /// </summary>
    int ConfigurableTypeCount { get; set; }

    /// <summary>
    /// Gets or sets the number of commands.
    /// </summary>
    int CommandCount { get; set; }
}

/// <summary>
/// Lists the configurable types of a catalog: the party features and their options, the client features and their
/// options, the App aspects.
/// </summary>
public interface IGetConfigurableTypesQCommand : ICommand<IList<IConfigurableTypeInfo>>, ICommandCatalogSource
{
}

/// <summary>
/// A configurable type: the configuration names it, configures it and switches it.
/// </summary>
public interface IConfigurableTypeInfo : IPoco
{
    /// <summary>
    /// Gets or sets the configuration name ("HttpEndpoint").
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the kind: "PartyFeature", "PartyFeatureOption", "PartyClientFeature", "PartyClientFeatureOption",
    /// "AppAspect"...
    /// </summary>
    string Kind { get; set; }

    /// <summary>
    /// Gets or sets the activation: "Optional", "ByDefault" or "Required".
    /// </summary>
    string Activation { get; set; }

    /// <summary>
    /// Gets or sets the full name of the type (the name of its package in the ResourceSpace, see <see cref="IGetClosureQCommand"/>).
    /// </summary>
    string TypeName { get; set; }

    /// <summary>
    /// Gets or sets the NuGet package that offers it.
    /// </summary>
    string PackageId { get; set; }

    /// <summary>
    /// Gets or sets the section of the type when it differs from its name.
    /// </summary>
    string? Section { get; set; }

    /// <summary>
    /// Gets the paths of the values that each deployment must write ("Party:Urls": the kind of the parties, then the path
    /// in the section).
    /// </summary>
    IList<string> PerDeployment { get; }

    /// <summary>
    /// Gets the paths of the secrets.
    /// </summary>
    IList<string> Secrets { get; }

    /// <summary>
    /// Gets the restrictions that the type declares.
    /// </summary>
    IList<ILockInfo> Locks { get; }

    /// <summary>
    /// Gets the capabilities of its properties.
    /// </summary>
    IList<IPropertyLevelInfo> PropertyLevels { get; }

    /// <summary>
    /// Gets the full names of the components of which it is an option (empty when it is not an option).
    /// </summary>
    IList<string> OptionOwners { get; }
}

/// <summary>
/// A restriction declared by a configurable type.
/// </summary>
public interface ILockInfo : IPoco
{
    /// <summary>
    /// Gets or sets the path (the kind of the parties, then the path in the section of the type).
    /// </summary>
    string Path { get; set; }

    /// <summary>
    /// Gets or sets the level: "Compile", "Init" or "Run".
    /// </summary>
    string Level { get; set; }

    /// <summary>
    /// Gets or sets the value that the restriction burns in, if any.
    /// </summary>
    string? Value { get; set; }
}

/// <summary>
/// The capability of a property of a configurable type: the highest level its code supports.
/// </summary>
public interface IPropertyLevelInfo : IPoco
{
    /// <summary>
    /// Gets or sets the property name.
    /// </summary>
    string Property { get; set; }

    /// <summary>
    /// Gets or sets the level: "Compile", "Init" or "Run".
    /// </summary>
    string Level { get; set; }
}

/// <summary>
/// Computes what a selection brings in: the packages that a ResourceSpace keeps from the resource groups of some NuGet
/// packages when some of them are the roots (the required references are kept, the optional ones never keep a package,
/// a reference to a single abstraction keeps its implementation). Returns their full names, ordered.
/// </summary>
public interface IGetClosureQCommand : ICommand<IList<string>>, ICommandCatalogSource
{
    /// <summary>
    /// Gets the full names of the roots (see <see cref="IConfigurableTypeInfo.TypeName"/>).
    /// </summary>
    IList<string> Roots { get; }

    /// <summary>
    /// Gets the NuGet packages whose resource groups are the candidates.
    /// </summary>
    IList<string> Packages { get; }
}

/// <summary>
/// Lists the commands of a catalog.
/// </summary>
public interface IGetCommandsQCommand : ICommand<IList<ICommandInfo>>, ICommandCatalogSource
{
}

/// <summary>
/// A command of a catalog: the family of its command interfaces.
/// </summary>
public interface ICommandInfo : IPoco
{
    /// <summary>
    /// Gets or sets the name of the command (its external name or the C# name of its primary interface).
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets who may send it: "Public", "Internal" (inside the System) or "Private" (inside the process).
    /// </summary>
    string Exposure { get; set; }

    /// <summary>
    /// Gets or sets the full name of its primary interface.
    /// </summary>
    string PrimaryInterface { get; set; }

    /// <summary>
    /// Gets the NuGet packages of its interfaces (the primary one first).
    /// </summary>
    IList<string> Packages { get; }
}
