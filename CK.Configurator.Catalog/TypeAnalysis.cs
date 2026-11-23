using System.Collections.Generic;

namespace CK.Configurator.Catalog;

/// <summary>
/// The declarations of a type that matter to the configurator. A type that declares nothing is not in the analysis.
/// </summary>
public sealed class TypeAnalysis
{
    /// <summary>
    /// Gets the full name of the type (<see cref="System.Type.FullName"/>): the name by which the ResourceSpace
    /// identifies a package and the name based relations (<c>[Requires( "..." )]</c>) designate it.
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// Gets the C# name of the type (for display).
    /// </summary>
    public required string CSharpName { get; init; }

    /// <summary>
    /// Gets whether the type is a resource group, a resource package or neither.
    /// </summary>
    public ResourceGroupKind ResourceGroup { get; init; }

    /// <summary>
    /// Gets the configurable type declaration (<c>IConfigurableTypeAttribute</c>) when the type is configurable.
    /// </summary>
    public ConfigurableType? Configurable { get; init; }

    /// <summary>
    /// Gets the section of the configurable type when it differs from its name (<c>[ConfigurationSection]</c>).
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// Gets the restrictions that the type declares (<c>[ConfigurationLock]</c>).
    /// </summary>
    public IReadOnlyList<ConfigurationLock> Locks { get => field ?? []; init; }

    /// <summary>
    /// Gets the paths of the secrets (<c>[ConfigurationSecret]</c>).
    /// </summary>
    public IReadOnlyList<string> Secrets { get => field ?? []; init; }

    /// <summary>
    /// Gets the paths of the values that each deployment must write (<c>[ConfigurationPerDeployment]</c>).
    /// </summary>
    public IReadOnlyList<string> PerDeployment { get => field ?? []; init; }

    /// <summary>
    /// Gets the capabilities of the properties of the type (<c>[ConfigurationLevel]</c>).
    /// </summary>
    public IReadOnlyList<PropertyLevel> PropertyLevels { get => field ?? []; init; }

    /// <summary>
    /// Gets the topology relations of the type, as declared.
    /// </summary>
    public IReadOnlyList<TypeRelation> Relations { get => field ?? []; init; }

    /// <summary>
    /// Gets the full names of the owners (an aspect, a party feature, a client feature) of which the type is an option
    /// (<c>IConfigurableOption&lt;TOwner&gt;</c>): the engine of a level attaches an option to its owner (a relation that
    /// the ResourceSpace doesn't see). An abstract owner stands for each of its concrete implementations.
    /// </summary>
    public IReadOnlyList<string> OptionOwners { get => field ?? []; init; }

    /// <summary>
    /// Gets the client application facts (<c>[PartyClientFact]</c>).
    /// </summary>
    public IReadOnlyList<ClientFact> Facts { get => field ?? []; init; }

    /// <summary>
    /// Gets the TypeScript package declaration when the type is a TypeScript package.
    /// </summary>
    public TypeScriptPackage? TypeScriptPackage { get; init; }

    /// <summary>
    /// Gets the command declaration when the type is a command interface.
    /// </summary>
    public CommandDeclaration? Command { get; init; }

    /// <summary>
    /// Gets the &lt;summary&gt; of the Xml documentation of the type, in Markdown (null when it has none).
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Gets the &lt;remarks&gt; of the Xml documentation of the type, in Markdown (null when it has none).
    /// </summary>
    public string? Remarks { get; init; }

    /// <summary>
    /// Returns the <see cref="CSharpName"/>.
    /// </summary>
    /// <returns>The C# name.</returns>
    public override string ToString() => CSharpName;
}

/// <summary>
/// Whether a type is a resource group (<c>IResourceGroup</c>), a resource package (<c>IResourcePackage</c>) or neither.
/// </summary>
public enum ResourceGroupKind
{
    /// <summary>
    /// Neither a group nor a package.
    /// </summary>
    None,

    /// <summary>
    /// A resource group.
    /// </summary>
    Group,

    /// <summary>
    /// A resource package (a group with resources).
    /// </summary>
    Package
}

/// <summary>
/// A configurable type: the configuration names it, configures it and switches it.
/// </summary>
/// <param name="Kind">The attribute without its "Attribute" suffix: "PartyFeature", "PartyClientFeatureOption", "AppAspect"...</param>
/// <param name="Name">The configuration name.</param>
/// <param name="Activation">The activation: "Optional", "ByDefault" or "Required".</param>
public sealed record ConfigurableType( string Kind, string Name, string Activation );

/// <summary>
/// A restriction declared by a type.
/// </summary>
/// <param name="Path">The path (the kind of the parties, then the path in the section of the type).</param>
/// <param name="Level">The level: "Compile", "Init" or "Run".</param>
/// <param name="Value">The optional value that the restriction burns in.</param>
public sealed record ConfigurationLock( string Path, string Level, string? Value );

/// <summary>
/// The capability of a property.
/// </summary>
/// <param name="Property">The property name.</param>
/// <param name="Level">The highest level the code supports: "Compile", "Init" or "Run".</param>
public sealed record PropertyLevel( string Property, string Level );

/// <summary>
/// A topology relation as declared by a type.
/// </summary>
/// <param name="Kind">"Requires", "RequiredBy", "Children", "Groups" or "Package".</param>
/// <param name="Target">The full name of the target (as written in a name based attribute, the <see cref="System.Type.FullName"/> otherwise).</param>
/// <param name="IsOptional">True for the [Optional...] relations.</param>
public sealed record TypeRelation( string Kind, string Target, bool IsOptional );

/// <summary>
/// A fact that a client application states.
/// </summary>
/// <param name="Key">The fact key.</param>
/// <param name="Value">The fact value (can be empty).</param>
public sealed record ClientFact( string Key, string Value );

/// <summary>
/// A TypeScript package declaration.
/// </summary>
/// <param name="Kind">The attribute without its "Attribute" suffix ("TypeScriptPackage" or a specialization).</param>
/// <param name="IsOptional">Whether the package is optional.</param>
/// <param name="TypeScriptFolder">The optional TypeScript folder.</param>
public sealed record TypeScriptPackage( string Kind, bool IsOptional, string? TypeScriptFolder );

/// <summary>
/// A command interface as declared (a command, a command part, an abstract base): the families are computed by the
/// catalog (see <see cref="CommandFamily"/>).
/// </summary>
/// <param name="IsAbstract">
/// True for an abstract interface (a definer: a command part, a base like <c>ICommandTargetParty</c>): it is never a
/// member of a family, it only takes part in the exposure of the families that extend it.
/// </param>
/// <param name="Bases">The full names of the direct base interfaces (the C# name of a generic one: <c>CK.Cris.ICommand&lt;CK.Cris.ICrisBasicCommandResult&gt;</c>).</param>
/// <param name="Exposure">"Public", "Internal" or "Private" (the declaration on this interface only).</param>
/// <param name="ExternalName">The name of the <c>[ExternalName]</c> of the interface (only the primary interface of a family should have one).</param>
public sealed record CommandDeclaration( bool IsAbstract, IReadOnlyList<string> Bases, string Exposure, string? ExternalName )
{
    /// <summary>
    /// Gets the full names of the direct base interfaces (an empty list is not written in the JSON: it is read as null).
    /// </summary>
    public IReadOnlyList<string> Bases { get; init; } = Bases ?? [];
}
