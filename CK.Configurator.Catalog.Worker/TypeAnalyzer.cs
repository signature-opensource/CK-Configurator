using CK.CodeGen;
using CK.Core;
using CK.TypeCache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CK.Configurator.Catalog.Worker;

/// <summary>
/// Reads what an assembly declares for the configurator.
/// <para>
/// The attributes of CK.AppBuilder.Abstractions (the configurable types, the configuration declarations) are known
/// types; the topology is read by <see cref="ResPackageRelationReader"/> (the rules and the checks of the ResourceSpace).
/// The others are read by their name: the ones of the packages that the analyzer doesn't reference (CK.TypeScript's
/// <c>[TypeScriptPackage]</c>, CK.Cris' commands) and <c>[PartyClientFact]</c> (an older profile has it in
/// CK.AppIdentity.PartyClient).
/// </para>
/// <para>
/// The configuration model of a type (<c>[Configuration&lt;TModel&gt;]</c>, read by its name) gives its JSON Schema:
/// <see cref="ConfigurationModelSchema"/> on the description of the model, built by reflection
/// (<see cref="ConfigurationModelDescription.TryGet"/>; its documentation from the Xml documentation file of the package).
/// </para>
/// <para>
/// The Xml documentation of a type (the ".xml" file next to its assembly: the worker project copies the documentation
/// files of the packages) is read by CK.CodeGen's <see cref="XmlDocumentationReader"/> and its &lt;summary&gt; and
/// &lt;remarks&gt; are converted to Markdown by its <see cref="DocumentationTextBuilder"/>.
/// </para>
/// <para>
/// The source files of a type and of its configuration model are found in the PDB of their assembly
/// (<see cref="SourceLocator"/>).
/// </para>
/// </summary>
sealed class TypeAnalyzer
{
    const string _partyClientFact = "CK.AppIdentity.PartyClientFactAttribute";
    const string _typeScriptPackage = "CK.TypeScript.TypeScriptPackageAttribute";
    const string _abstractCommand = "CK.Cris.IAbstractCommand";
    const string _commandPart = "CK.Cris.ICommandPart";
    const string _internal = "CK.Cris.InternalAttribute";
    const string _private = "CK.Cris.PrivateAttribute";
    const string _externalName = "CK.Core.ExternalNameAttribute";
    const string _configurableOption = "CK.Core.IConfigurableOption`1";
    // Read by name: an older CK.ResourceSpace.Abstractions (in an older profile) doesn't have it.
    const string _centralKey = "CK.Core.CentralConfigurationKeyAttribute";
    // Read by name: an older CK.Abstractions (in an older profile) doesn't have it.
    const string _configuration = "CK.Core.ConfigurationAttribute`1";
    const string _compileConfiguration = "CK.Core.CompileConfigurationAttribute`1";

    readonly IActivityMonitor _monitor;
    readonly GlobalTypeCache _typeCache;
    readonly Dictionary<object, object?> _documentationCache;
    readonly SourceLocator _sources;
    bool _success;

    public TypeAnalyzer( IActivityMonitor monitor, GlobalTypeCache typeCache )
    {
        _monitor = monitor;
        _typeCache = typeCache;
        _documentationCache = new Dictionary<object, object?>();
        _sources = new SourceLocator( monitor );
        _success = true;
    }

    /// <summary>
    /// Gets whether no error occurred.
    /// </summary>
    public bool Success => _success;

    public AssemblyAnalysis Analyze( CachedAssembly assembly )
    {
        var kind = assembly.Kind;
        var role = kind.IsEngine()
                    ? AssemblyRole.Engine
                    : kind.IsModel()
                        ? AssemblyRole.Model
                        : kind.IsModelDefiner()
                            ? AssemblyRole.ModelDefiner
                            : AssemblyRole.Basic;
        var types = new List<TypeAnalysis>();
        if( role == AssemblyRole.Model )
        {
            foreach( var t in GetModelTypes( assembly, _typeCache ).OrderBy( t => t.Type.FullName, StringComparer.Ordinal ) )
            {
                var a = AnalyzeType( t );
                if( a != null ) types.Add( a );
            }
        }
        // The assembly declarations: read on any assembly (an engine declares the structure it reads).
        var perDeployment = new List<string>();
        var centralKeys = new List<CentralKey>();
        foreach( var a in assembly.CustomAttributes )
        {
            if( a.AttributeType == typeof( ConfigurationPerDeploymentAttribute ) )
            {
                perDeployment.Add( (string)a.ConstructorArguments[0].Value! );
            }
            else if( a.AttributeType.FullName == _centralKey )
            {
                var patterns = GetNamed( a, "PerDeployment" ) is IReadOnlyCollection<CustomAttributeTypedArgument> p
                                ? p.Select( v => (string)v.Value! ).ToList()
                                : [];
                centralKeys.Add( new CentralKey( (string)a.ConstructorArguments[0].Value!, patterns ) );
            }
        }
        return new AssemblyAnalysis
        {
            Name = assembly.Name,
            Role = role,
            RequiredEngines = assembly.RequiredEngines.Select( e => new RequiredEngine( e.Name, e.VersionRange ) ).ToList(),
            Types = types,
            PerDeployment = perDeployment,
            CentralKeys = centralKeys
        };
    }

    /// <summary>
    /// Gets the types of a model: its type set (its [RegisterCKType]/[ExcludeCKType] applied) when the assembly cache
    /// computed it, its exported types otherwise (a model that no head uses).
    /// </summary>
    /// <param name="assembly">The model assembly.</param>
    /// <param name="typeCache">The type cache.</param>
    /// <returns>The types of the model.</returns>
    public static IEnumerable<ICachedType> GetModelTypes( CachedAssembly assembly, GlobalTypeCache typeCache )
    {
        return assembly.Types != null
                ? assembly.Types.AllTypes.Where( t => t.Assembly == assembly )
                : assembly.Assembly.GetExportedTypes().Select( typeCache.Get );
    }

    TypeAnalysis? AnalyzeType( ICachedType t )
    {
        var type = t.Type;
        if( type.FullName == null ) return null;
        // A command interface is analyzed even when it is abstract (a definer: a command part, a base like
        // ICommandTargetParty): the exposure of a family is the most restrictive one of all its interfaces.
        bool isCommandInterface = type.IsInterface
                                  && t.Interfaces.Any( i => i.Type.FullName == _abstractCommand || i.Type.FullName == _commandPart );
        bool isAbstract = t.IsTypeDefiner || t.IsSuperTypeDefiner;
        if( isAbstract && !isCommandInterface ) return null;

        ConfigurableType? configurable = null;
        if( !t.TryGetConfigurableType( _monitor, out var c ) )
        {
            _success = false;
        }
        else if( c != null )
        {
            configurable = new ConfigurableType( WithoutAttributeSuffix( c.GetType().Name ), c.Name, c.Activation.ToString() );
        }

        string? section = null;
        List<ConfigurationLock>? locks = null;
        List<string>? secrets = null;
        List<string>? perDeployment = null;
        List<TypeRelation>? relations = null;
        List<ClientFact>? facts = null;
        TypeScriptPackage? typeScriptPackage = null;
        bool isInternal = false;
        bool isPrivate = false;
        string? externalName = null;
        Type? modelType = null;
        Type? compileModelType = null;

        foreach( var a in t.AttributesData )
        {
            var aType = a.AttributeType;
            if( aType == typeof( ConfigurationSectionAttribute ) )
            {
                section = (string?)a.ConstructorArguments[0].Value;
            }
            else if( aType == typeof( ConfigurationLockAttribute ) )
            {
                (locks ??= []).Add( new ConfigurationLock( (string)a.ConstructorArguments[0].Value!,
                                                           ((ConfigurationLevel)(int)a.ConstructorArguments[1].Value!).ToString(),
                                                           (string?)GetNamed( a, "Value" ) ) );
            }
            else if( aType == typeof( ConfigurationSecretAttribute ) )
            {
                (secrets ??= []).Add( (string)a.ConstructorArguments[0].Value! );
            }
            else if( aType == typeof( ConfigurationPerDeploymentAttribute ) )
            {
                (perDeployment ??= []).Add( (string)a.ConstructorArguments[0].Value! );
            }
            else if( aType.IsGenericType && aType.GetGenericTypeDefinition().FullName == _configuration )
            {
                modelType = aType.GetGenericArguments()[0];
            }
            else if( aType.IsGenericType && aType.GetGenericTypeDefinition().FullName == _compileConfiguration )
            {
                compileModelType = aType.GetGenericArguments()[0];
            }
            else if( aType.FullName == _partyClientFact )
            {
                (facts ??= []).Add( new ClientFact( (string)a.ConstructorArguments[0].Value!,
                                                    a.ConstructorArguments.Count > 1 ? (string?)a.ConstructorArguments[1].Value ?? "" : "" ) );
            }
            else if( aType.FullName == _internal )
            {
                isInternal = true;
            }
            else if( aType.FullName == _private )
            {
                isPrivate = true;
            }
            else if( IsOrSpecializes( aType, _externalName ) )
            {
                externalName = (string?)a.ConstructorArguments[0].Value;
            }
            else if( IsOrSpecializes( aType, _typeScriptPackage ) )
            {
                typeScriptPackage = new TypeScriptPackage( WithoutAttributeSuffix( aType.Name ),
                                                           GetNamed( a, "IsOptional" ) is true,
                                                           (string?)GetNamed( a, "TypeScriptFolder" ) );
            }
        }
        // The relations: read by the reader of the ResourceSpace (the rules and the checks of the space).
        var relationCollector = new RelationCollector();
        if( !ResPackageRelationReader.Read( _monitor, t, ref relationCollector ) ) _success = false;
        relations = relationCollector.Relations;

        List<PropertyLevel>? levels = null;
        foreach( var p in type.GetProperties( BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
        {
            foreach( var a in p.CustomAttributes )
            {
                if( a.AttributeType == typeof( ConfigurationLevelAttribute ) )
                {
                    (levels ??= []).Add( new PropertyLevel( p.Name, ((ConfigurationLevel)(int)a.ConstructorArguments[0].Value!).ToString() ) );
                }
            }
        }

        CommandDeclaration? command = null;
        if( isCommandInterface )
        {
            command = new CommandDeclaration( isAbstract,
                                              // A generic base (ICommand<TResult>) by its C# name: its full name is assembly qualified.
                                              t.DirectInterfaces.Select( i => i.Type.IsGenericType ? i.CSharpName : i.Type.FullName ?? i.CSharpName ).ToList(),
                                              isPrivate ? "Private" : isInternal ? "Internal" : "Public",
                                              externalName?.Trim() );
        }

        List<string>? optionOwners = null;
        foreach( var i in t.Interfaces )
        {
            if( i.Type.IsGenericType && i.Type.GetGenericTypeDefinition().FullName == _configurableOption )
            {
                var owner = i.Type.GetGenericArguments()[0].FullName;
                if( owner != null ) (optionOwners ??= []).Add( owner );
            }
        }

        var groupKind = typeof( IResourcePackage ).IsAssignableFrom( type )
                            ? ResourceGroupKind.Package
                            : typeof( IResourceGroup ).IsAssignableFrom( type )
                                ? ResourceGroupKind.Group
                                : ResourceGroupKind.None;

        if( configurable == null && section == null && locks == null && secrets == null && perDeployment == null
            && relations == null && facts == null && typeScriptPackage == null && levels == null && command == null
            && optionOwners == null && modelType == null && compileModelType == null
            && groupKind == ResourceGroupKind.None )
        {
            return null;
        }
        var documentation = XmlDocumentationReader.GetDocumentationFor( _monitor, type, _documentationCache );
        var schema = modelType != null ? GetSchema( type, modelType ) : null;
        var compileSchema = compileModelType != null ? GetSchema( type, compileModelType ) : null;
        return new TypeAnalysis
        {
            FullName = type.FullName,
            CSharpName = t.CSharpName,
            ResourceGroup = groupKind,
            Configurable = configurable,
            Section = section,
            Locks = (IReadOnlyList<ConfigurationLock>?)locks ?? [],
            Secrets = (IReadOnlyList<string>?)secrets ?? [],
            PerDeployment = (IReadOnlyList<string>?)perDeployment ?? [],
            PropertyLevels = (IReadOnlyList<PropertyLevel>?)levels ?? [],
            Relations = (IReadOnlyList<TypeRelation>?)relations ?? [],
            Facts = (IReadOnlyList<ClientFact>?)facts ?? [],
            OptionOwners = (IReadOnlyList<string>?)optionOwners ?? [],
            TypeScriptPackage = typeScriptPackage,
            Command = command,
            ConfigurationModel = modelType?.FullName,
            ConfigurationSchema = schema,
            CompileConfigurationModel = compileModelType?.FullName,
            CompileConfigurationSchema = compileSchema,
            SourceUrl = _sources.GetSourceUrl( type ),
            ConfigurationModelSourceUrl = modelType != null ? _sources.GetSourceUrl( modelType ) : null,
            Summary = ToMarkdown( documentation?.Element( "summary" ) ),
            Remarks = ToMarkdown( documentation?.Element( "remarks" ) )
        };
    }

    // The generated description of a model (its static ModelDescription) gives its JSON Schema.
    JsonObject? GetSchema( Type type, Type modelType )
    {
        try
        {
            if( ConfigurationModelDescription.TryGet( modelType, out var d, out var error ) )
            {
                return ConfigurationModelSchema.ToJsonSchema( d );
            }
            _monitor.Warn( $"The schema of the configuration of '{type:N}' is unknown. {error}" );
        }
        catch( Exception ex )
        {
            _monitor.Warn( $"Unable to get the schema of the configuration model '{modelType:N}' of '{type:N}'.", ex );
        }
        return null;
    }

    static string? ToMarkdown( XElement? e )
    {
        if( e == null ) return null;
        var text = new DocumentationTextBuilder().AppendLinesFromXElement( e ).ToString();
        return text.Length == 0 ? null : text;
    }

    static object? GetNamed( CustomAttributeData a, string name )
    {
        foreach( var n in a.NamedArguments )
        {
            if( n.MemberName == name ) return n.TypedValue.Value;
        }
        return null;
    }

    static bool IsOrSpecializes( Type? t, string fullName )
    {
        while( t != null )
        {
            if( t.FullName == fullName ) return true;
            t = t.BaseType;
        }
        return false;
    }

    static string WithoutAttributeSuffix( string name )
    {
        int i = name.IndexOf( '`' );
        if( i >= 0 ) name = name[..i];
        return name.EndsWith( "Attribute", StringComparison.Ordinal ) ? name[..^"Attribute".Length] : name;
    }

    struct RelationCollector : IResPackageRelationVisitor
    {
        public List<TypeRelation>? Relations;

        public void OnPackage( ResPackageDescriptor.Ref package )
        {
            (Relations ??= []).Add( new TypeRelation( "Package", package.FullName!, false ) );
        }

        public void OnRelation( ResRelationKind kind, ResPackageDescriptor.Ref target )
        {
            (Relations ??= []).Add( new TypeRelation( kind.ToString(), target.FullName!, target.IsOptional ) );
        }
    }
}
