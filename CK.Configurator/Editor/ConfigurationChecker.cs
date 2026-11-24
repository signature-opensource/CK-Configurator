using CK.Configurator.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// The static checks of a central configuration (the first layer of the validation of the editor): the properties of
/// the document are typed by the catalog (their <see cref="EditorNodeData.Role"/>) and checked with the rules of the
/// build, without running anything.
/// <list type="bullet">
///   <item>
///   The structure: the root keys (<c>$schema</c>, <c>$configurator</c>, <c>Deployment</c>, <c>Builders</c>,
///   <c>Defaults</c> and the keys that the packages declare: <c>[assembly: CentralConfigurationKey]</c>), the keys of a
///   builder (<c>App</c>, <c>Apps</c>, <c>Aspects</c>, <c>ProjectCompileKeys</c>), the levels of an App (<c>Aspects</c>,
///   <c>Local</c>, <c>Defaults</c>).
///   </item>
///   <item>
///   The configurable types: a Builder aspect, an App aspect, a party feature, a client feature and their options must be
///   known by the catalog (a key of a party that is no feature is left to the run time by the build: a warning); an
///   option under another owner is a warning; a required type can't be <c>false</c>; a configurable type is
///   <c>true</c>, <c>false</c> or a section.
///   </item>
///   <item>
///   The syntax of <c>"$LOCK"</c> (levels "Compile", "Init" or "Run"), <c>"$PER_DEPLOYMENT"</c> (an array of paths) and
///   <c>{ "$SECRET": "..." }</c> (the only key of its object); a secret of a type written as a plain value.
///   </item>
/// </list>
/// The keys of a section are free (a section is not declared yet, unless its type has a configuration model).
/// </summary>
public static class ConfigurationChecker
{
    // "$PER_DEPLOYMENT" markers are accepted on any object (CentralDeployment.PerDeploymentKey).
    static readonly HashSet<string> _rootKeys = new( StringComparer.OrdinalIgnoreCase ) { "$schema", "$configurator", "$PER_DEPLOYMENT", "Deployment", "Builders", "Defaults" };
    static readonly HashSet<string> _builderKeys = new( StringComparer.OrdinalIgnoreCase ) { "App", "Apps", "Aspects", "ProjectCompileKeys", "$schema", "$PER_DEPLOYMENT" };
    static readonly HashSet<string> _partyKeys = new( StringComparer.OrdinalIgnoreCase ) { "PublicUrl", "Assemblies", "DefaultAssembly" };
    static readonly HashSet<string> _clientKeys = new( StringComparer.OrdinalIgnoreCase ) { "Types", "ExcludedTypes" };
    static readonly HashSet<string> _lockLevels = new( StringComparer.Ordinal ) { "\"Compile\"", "\"Init\"", "\"Run\"" };

    /// <summary>
    /// Checks the properties of a document with a catalog.
    /// </summary>
    /// <param name="nodes">The properties of the document, in document order.</param>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The typed properties and the problems.</returns>
    public static (IReadOnlyList<EditorNodeData> Nodes, IReadOnlyList<EditorProblemData> Problems) Check( IReadOnlyList<EditorNodeData> nodes,
                                                                                                         CatalogModel catalog )
    {
        var c = new Checker( nodes, catalog );
        c.Run();
        return (nodes.Select( n => c.Roles.TryGetValue( n.Path, out var r ) ? n with { Role = r.Role, TypeName = r.TypeName } : n ).ToList(),
                c.Problems);
    }

    sealed class Checker
    {
        readonly CatalogModel _catalog;
        readonly Dictionary<string, List<EditorNodeData>> _children = new( StringComparer.OrdinalIgnoreCase );
        readonly HashSet<string> _centralKeys;
        public readonly Dictionary<string, (string Role, string? TypeName)> Roles = new( StringComparer.OrdinalIgnoreCase );
        public readonly List<EditorProblemData> Problems = new();

        public Checker( IReadOnlyList<EditorNodeData> nodes, CatalogModel catalog )
        {
            _catalog = catalog;
            foreach( var n in nodes )
            {
                var parent = ParentPath( n.Path );
                if( !_children.TryGetValue( parent, out var list ) ) _children.Add( parent, list = new List<EditorNodeData>() );
                list.Add( n );
            }
            _centralKeys = new HashSet<string>( catalog.Packages.SelectMany( p => p.Assemblies ).SelectMany( a => a.CentralKeys ).Select( k => k.Key ),
                                                StringComparer.OrdinalIgnoreCase );
        }

        static string ParentPath( string path )
        {
            int i = path.LastIndexOf( ':' );
            return i < 0 ? "" : path.Substring( 0, i );
        }

        IReadOnlyList<EditorNodeData> Children( string path ) => _children.TryGetValue( path, out var l ) ? l : [];

        void Error( string path, string message ) => Problems.Add( new EditorProblemData( path, "Error", message ) );
        void Warn( string path, string message ) => Problems.Add( new EditorProblemData( path, "Warning", message ) );

        public void Run()
        {
            foreach( var n in Children( "" ) )
            {
                if( n.Name.Equals( "$configurator", StringComparison.OrdinalIgnoreCase ) ) CheckConfigurator( n );
                else if( n.Name.Equals( "Builders", StringComparison.OrdinalIgnoreCase ) ) CheckBuilders( n );
                else if( n.Name.Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) ) CheckDefaults( n );
                // A "$LOCK" is reported by CheckSyntax (it is outside the Apps and the Builder aspects).
                else if( !_rootKeys.Contains( n.Name ) && !_centralKeys.Contains( n.Name ) && !n.Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) )
                {
                    Warn( n.Path, $"Unknown root key '{n.Name}': no package of the catalog declares it (the build refuses a key that no contributor claims)." );
                }
            }
            CheckSyntax();
        }

        void CheckConfigurator( EditorNodeData n )
        {
            var keys = Children( n.Path );
            if( n.Kind != "Object"
                || keys.Count != 2
                || keys.Any( k => k.Kind != "String" || !(k.Name.Equals( "World", StringComparison.OrdinalIgnoreCase ) || k.Name.Equals( "Version", StringComparison.OrdinalIgnoreCase )) ) )
            {
                Error( n.Path, "\"$configurator\" must be { \"World\": \"...\", \"Version\": \"...\" } (the source of the catalog)." );
            }
        }

        void CheckDefaults( EditorNodeData n )
        {
            foreach( var c in Children( n.Path ) )
            {
                if( c.Name.Equals( "App", StringComparison.OrdinalIgnoreCase ) ) CheckApp( c );
                else if( !c.Name.Equals( "$PER_DEPLOYMENT", StringComparison.OrdinalIgnoreCase ) && !c.Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) )
                {
                    Warn( c.Path, $"Unknown key '{c.Name}' in \"Defaults\": the defaults are the ones of every App (\"Defaults:App\")." );
                }
            }
        }

        void CheckBuilders( EditorNodeData builders )
        {
            foreach( var b in Children( builders.Path ) )
            {
                Roles[b.Path] = ("Builder", null);
                if( b.Kind != "Object" )
                {
                    Error( b.Path, $"The builder '{b.Name}' must be an object." );
                    continue;
                }
                foreach( var k in Children( b.Path ) )
                {
                    if( k.Name.Equals( "Aspects", StringComparison.OrdinalIgnoreCase ) ) CheckAspects( k, "AppBuilderAspect", "BuilderAspect", "a Builder aspect" );
                    else if( k.Name.Equals( "App", StringComparison.OrdinalIgnoreCase ) ) CheckApp( k );
                    else if( !_builderKeys.Contains( k.Name ) )
                    {
                        Error( k.Path, $"Unknown key '{k.Name}' in the builder '{b.Name}': its keys are App (or Apps), Aspects and ProjectCompileKeys." );
                    }
                }
            }
        }

        void CheckApp( EditorNodeData app )
        {
            Roles[app.Path] = ("App", null);
            foreach( var k in Children( app.Path ) )
            {
                if( k.Name.Equals( "Aspects", StringComparison.OrdinalIgnoreCase ) ) CheckAspects( k, "AppAspect", "AppAspect", "an App aspect" );
                else if( k.Name.Equals( "Local", StringComparison.OrdinalIgnoreCase ) ) CheckParty( k, "Party", isLocal: true );
                else if( k.Name.Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) ) CheckParty( k, "PartyDefaults", isLocal: false );
            }
        }

        void CheckAspects( EditorNodeData aspects, string kind, string role, string description )
        {
            foreach( var a in Children( aspects.Path ) )
            {
                if( a.Name.StartsWith( '$' ) ) continue;
                var t = FindConfigurable( a.Name, kind );
                if( t == null )
                {
                    Warn( a.Path, $"'{a.Name}' is not {description} of the catalog." );
                    continue;
                }
                Typed( a, role, t, partyKind: null );
            }
        }

        void CheckParty( EditorNodeData party, string role, bool isLocal )
        {
            Roles[party.Path] = (role, null);
            foreach( var k in Children( party.Path ) )
            {
                if( k.Name.StartsWith( '$' ) || _partyKeys.Contains( k.Name ) ) continue;
                var t = FindConfigurable( k.Name, "PartyFeature" );
                if( t == null )
                {
                    Warn( k.Path, $"'{k.Name}' is not a party feature of the catalog: the build leaves it to the run time." );
                    continue;
                }
                Typed( k, "PartyFeature", t, isLocal ? "Local" : "Party" );
                if( k.Kind != "Object" ) continue;
                if( k.Name.Equals( "PartyClient", StringComparison.OrdinalIgnoreCase ) )
                {
                    foreach( var client in Children( k.Path ) ) CheckClient( client );
                }
                else
                {
                    CheckOptions( k, t, "PartyFeatureOption", isLocal ? "Local" : "Party" );
                }
            }
        }

        void CheckClient( EditorNodeData client )
        {
            Roles[client.Path] = ("Client", null);
            if( client.Kind is "Boolean" or "Null" ) return;
            if( client.Kind != "Object" )
            {
                Error( client.Path, $"The client '{client.Name}' must be an object." );
                return;
            }
            foreach( var k in Children( client.Path ) )
            {
                if( k.Name.StartsWith( '$' ) || _clientKeys.Contains( k.Name ) ) continue;
                var t = FindConfigurable( k.Name, "PartyClientFeature" );
                if( t == null )
                {
                    Warn( k.Path, $"'{k.Name}' is not a client feature of the catalog." );
                    continue;
                }
                Typed( k, "ClientFeature", t, "Local" );
                if( k.Kind == "Object" ) CheckOptions( k, t, "PartyClientFeatureOption", "Local" );
            }
        }

        // The options of an owner: a key that names an option of another owner is a warning; the other keys are free.
        void CheckOptions( EditorNodeData owner, TypeAnalysis ownerType, string optionKind, string partyKind )
        {
            foreach( var k in Children( owner.Path ) )
            {
                if( k.Name.StartsWith( '$' ) ) continue;
                var options = _catalog.FindConfigurable( k.Name ).Where( c => c.Type.Configurable!.Kind == optionKind ).Select( c => c.Type ).ToList();
                if( options.Count == 0 ) continue;
                var mine = options.FirstOrDefault( o => o.OptionOwners.Contains( ownerType.FullName ) );
                if( mine == null )
                {
                    var owners = options.SelectMany( o => o.OptionOwners ).Distinct().Select( o => _catalog.FindType( o )?.Type.Configurable?.Name ?? o );
                    Warn( k.Path, $"'{k.Name}' is an option of '{string.Join( "', '", owners )}', not of '{owner.Name}'." );
                    continue;
                }
                Typed( k, optionKind == "PartyFeatureOption" ? "PartyFeatureOption" : "ClientFeatureOption", mine, partyKind );
            }
        }

        TypeAnalysis? FindConfigurable( string name, string kind )
        {
            return _catalog.FindConfigurable( name ).Where( c => c.Type.Configurable!.Kind == kind ).Select( c => c.Type ).FirstOrDefault();
        }

        // A configurable type: true, false or a section; a required one can't be false; its secrets are handles.
        void Typed( EditorNodeData n, string role, TypeAnalysis t, string? partyKind )
        {
            Roles[n.Path] = (role, t.Configurable!.Name);
            if( n.Kind is not ("Object" or "Boolean" or "Null") )
            {
                Error( n.Path, $"'{n.Name}' must be true, false or a section." );
                return;
            }
            if( n.Kind == "Boolean" && n.Value == "false" && t.Configurable.Activation == "Required" )
            {
                Error( n.Path, $"'{n.Name}' is required: it can't be disabled." );
            }
            if( n.Kind == "Object" && partyKind != null ) CheckSecrets( n, t, partyKind );
        }

        // A secret path is "{kind of the parties}:{path in the section}" ('*' matches any name): "Party" applies to every
        // party, "Local" to the local one.
        void CheckSecrets( EditorNodeData section, TypeAnalysis t, string partyKind )
        {
            foreach( var secret in t.Secrets )
            {
                int i = secret.IndexOf( ':' );
                if( i < 0 ) continue;
                var kind = secret.Substring( 0, i );
                if( kind != "Party" && kind != partyKind ) continue;
                foreach( var n in Match( section.Path, secret.Substring( i + 1 ).Split( ':' ), 0 ) )
                {
                    if( n.Kind is "String" or "Number" )
                    {
                        Warn( n.Path, $"'{n.Name}' is a secret: write a handle, {{ \"$SECRET\": \"<the key of the secret>\" }}, not its value." );
                    }
                }
            }
        }

        IEnumerable<EditorNodeData> Match( string path, string[] segments, int index )
        {
            foreach( var c in Children( path ) )
            {
                if( segments[index] != "*" && !segments[index].Equals( c.Name, StringComparison.OrdinalIgnoreCase ) ) continue;
                if( index == segments.Length - 1 ) yield return c;
                else foreach( var m in Match( c.Path, segments, index + 1 ) ) yield return m;
            }
        }

        // The "$LOCK", "$PER_DEPLOYMENT" and "$SECRET" of the whole document.
        void CheckSyntax()
        {
            foreach( var list in _children.Values )
            {
                foreach( var n in list )
                {
                    if( n.Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) )
                    {
                        if( !IsLockable( ParentPath( n.Path ) ) )
                        {
                            Error( n.Path, "A \"$LOCK\" restricts the values of an App or of a Builder aspect: it is outside them." );
                        }
                        else if( n.Kind != "Object" || Children( n.Path ).Any( l => l.Kind != "String" || !_lockLevels.Contains( l.Value! ) ) )
                        {
                            Error( n.Path, "\"$LOCK\" must be an object whose values are \"Compile\", \"Init\" or \"Run\"." );
                        }
                    }
                    else if( n.Name.Equals( "$PER_DEPLOYMENT", StringComparison.OrdinalIgnoreCase ) )
                    {
                        if( n.Kind != "Array" || !IsStringArray( n.Value! ) )
                        {
                            Error( n.Path, "\"$PER_DEPLOYMENT\" must be an array of paths (strings)." );
                        }
                    }
                    else if( n.Name.Equals( "$SECRET", StringComparison.OrdinalIgnoreCase ) || n.Name.Equals( "$SECRET?", StringComparison.OrdinalIgnoreCase ) )
                    {
                        if( n.Kind != "String" || list.Count != 1 )
                        {
                            Error( n.Path, $"\"{n.Name}\" must be the only key of its object and a string (the key of the secret)." );
                        }
                    }
                }
            }
        }

        // The objects where a "$LOCK" can be (AppBuilderConfiguration): in an App ("Builders:{b}:App", "Builders:{b}:Apps",
        // "Defaults:App") or in a Builder aspect ("Builders:{b}:Aspects:{aspect}", not "Aspects" itself).
        static bool IsLockable( string objectPath )
        {
            var s = objectPath.Split( ':' );
            if( s.Length >= 2 && s[0].Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) && s[1].Equals( "App", StringComparison.OrdinalIgnoreCase ) ) return true;
            if( s.Length < 3 || !s[0].Equals( "Builders", StringComparison.OrdinalIgnoreCase ) ) return false;
            if( s[2].Equals( "App", StringComparison.OrdinalIgnoreCase ) || s[2].Equals( "Apps", StringComparison.OrdinalIgnoreCase ) ) return true;
            return s.Length >= 4 && s[2].Equals( "Aspects", StringComparison.OrdinalIgnoreCase );
        }

        static bool IsStringArray( string json )
        {
            try
            {
                using var d = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } );
                return d.RootElement.ValueKind == JsonValueKind.Array && d.RootElement.EnumerateArray().All( e => e.ValueKind == JsonValueKind.String );
            }
            catch( JsonException )
            {
                return false;
            }
        }
    }
}
