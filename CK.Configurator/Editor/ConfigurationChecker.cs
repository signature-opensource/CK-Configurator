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
///   The structure: the root keys (<c>$schema</c>, <c>$configurator</c>, <c>Deployment</c>, <c>Domain</c>,
///   <c>GlobalDomainPrefix</c>, <c>AppContexts</c>, <c>Defaults</c> and the keys that the packages declare:
///   <c>[assembly: CentralConfigurationKey]</c>), the keys of an AppContext (<c>RootPath</c>, <c>App</c>, <c>Apps</c>,
///   <c>Aspects</c>, <c>ProjectCompileKeys</c>), the levels of an App (<c>Aspects</c>, <c>Local</c>, <c>Defaults</c>).
///   </item>
///   <item>
///   The configurable types: an AppContext aspect, an App aspect, a party feature, a client feature and their options must be
///   known by the catalog (a key of a party that is no feature is left to the run time by the build: a warning); an
///   option under another owner is a warning; a required type can't be <c>false</c>; a configurable type is
///   <c>true</c>, <c>false</c> or a section.
///   </item>
///   <item>
///   The required types of the options ("only with it", as the build selects the options): an option enabled explicitly
///   that requires a disabled type (another option of its owner, a party feature, a client feature, an App or an AppContext
///   aspect) is an error; an option enabled by default is disabled with it (an information on its owner), and so are
///   the options that require it in turn. A declaration is looked up as the run time does: in the party (or the client,
///   the aspects of the App), then in the "Defaults" above it, the central "Defaults:App" merged under each App.
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
    static readonly HashSet<string> _rootKeys = new( StringComparer.OrdinalIgnoreCase ) { "$schema", "$configurator", "$PER_DEPLOYMENT", "Deployment", "AppContexts", "Defaults" };
    static readonly HashSet<string> _appContextKeys = new( StringComparer.OrdinalIgnoreCase ) { "RootPath", "App", "Apps", "Aspects", "ProjectCompileKeys", "$schema", "$PER_DEPLOYMENT" };
    static readonly HashSet<string> _partyKeys = new( StringComparer.OrdinalIgnoreCase ) { "PublicUrl", "Assemblies", "DefaultAssembly" };
    static readonly HashSet<string> _clientKeys = new( StringComparer.OrdinalIgnoreCase ) { "Types", "ExcludedTypes" };
    static readonly HashSet<string> _lockLevels = new( StringComparer.Ordinal ) { "\"Compile\"", "\"Init\"", "\"Run\"" };

    /// <summary>
    /// Checks the properties of a document with a catalog.
    /// </summary>
    /// <param name="nodes">The properties of the document, in document order.</param>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The typed properties and the problems.</returns>
    /// <param name="isDeployment">True for the merged view of a deployment: its <c>"$BASE"</c> root key is accepted.</param>
    public static (IReadOnlyList<EditorNodeData> Nodes, IReadOnlyList<EditorProblemData> Problems) Check( IReadOnlyList<EditorNodeData> nodes,
                                                                                                         CatalogModel catalog,
                                                                                                         bool isDeployment = false )
    {
        var c = new Checker( nodes, catalog, isDeployment );
        c.Run();
        return (nodes.Select( n => c.Roles.TryGetValue( n.Path, out var r ) ? n with { Role = r.Role, TypeName = r.TypeName } : n ).ToList(),
                c.Problems.Distinct().ToList());
    }

    sealed class Checker
    {
        readonly CatalogModel _catalog;
        readonly Dictionary<string, List<EditorNodeData>> _children = new( StringComparer.OrdinalIgnoreCase );
        readonly Dictionary<string, EditorNodeData> _byPath = new( StringComparer.OrdinalIgnoreCase );
        readonly HashSet<string> _centralKeys;
        readonly bool _isDeployment;
        public readonly Dictionary<string, (string Role, string? TypeName)> Roles = new( StringComparer.OrdinalIgnoreCase );
        public readonly List<EditorProblemData> Problems = new();

        public Checker( IReadOnlyList<EditorNodeData> nodes, CatalogModel catalog, bool isDeployment )
        {
            _catalog = catalog;
            _isDeployment = isDeployment;
            // The parent of a property is the last one above it at a lower depth (document order): a key may contain ':'
            // (the patterns of a "$LOCK"), its path can't be cut at its last ':'.
            var ancestors = new List<string>();
            foreach( var n in nodes )
            {
                _byPath[n.Path] = n;
                if( ancestors.Count > n.Depth ) ancestors.RemoveRange( n.Depth, ancestors.Count - n.Depth );
                var parent = n.Depth == 0 || ancestors.Count < n.Depth ? ParentPath( n.Path ) : ancestors[n.Depth - 1];
                ancestors.Add( n.Path );
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
                else if( n.Name.Equals( "AppContexts", StringComparison.OrdinalIgnoreCase ) ) CheckAppContexts( n );
                else if( n.Name.Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) ) CheckDefaults( n );
                // A "$LOCK" is reported by CheckSyntax (it is outside the Apps and the AppContext aspects).
                else if( _isDeployment && n.Name.Equals( "$BASE", StringComparison.OrdinalIgnoreCase ) ) continue;
                else if( !_rootKeys.Contains( n.Name ) && !_centralKeys.Contains( n.Name ) && !n.Name.Equals( "$LOCK", StringComparison.OrdinalIgnoreCase ) )
                {
                    Warn( n.Path, $"Unknown root key '{n.Name}': no package of the catalog declares it (the build refuses a key that no contributor claims)." );
                }
            }
            CheckSyntax();
            CheckRequirements();
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

        void CheckAppContexts( EditorNodeData appContexts )
        {
            foreach( var b in Children( appContexts.Path ) )
            {
                Roles[b.Path] = ("AppContext", null);
                if( b.Kind != "Object" )
                {
                    Error( b.Path, $"The AppContext '{b.Name}' must be an object." );
                    continue;
                }
                foreach( var k in Children( b.Path ) )
                {
                    if( k.Name.Equals( "Aspects", StringComparison.OrdinalIgnoreCase ) ) CheckAspects( k, "AppContextAspect", "AppContextAspect", "an AppContext aspect" );
                    else if( k.Name.Equals( "App", StringComparison.OrdinalIgnoreCase ) ) CheckApp( k );
                    else if( !_appContextKeys.Contains( k.Name ) )
                    {
                        Error( k.Path, $"Unknown key '{k.Name}' in the AppContext '{b.Name}': its keys are RootPath, App (or Apps), Aspects and ProjectCompileKeys." );
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
                if( a.Kind == "Object" ) CheckOptions( a, t, kind + "Option", role + "Option", partyKind: null );
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
                    CheckOptions( k, t, "PartyFeatureOption", "PartyFeatureOption", isLocal ? "Local" : "Party" );
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
                if( k.Kind == "Object" ) CheckOptions( k, t, "PartyClientFeatureOption", "ClientFeatureOption", "Local" );
            }
        }

        // The options of an owner: a key that names an option of another owner is a warning; the other keys are free.
        void CheckOptions( EditorNodeData owner, TypeAnalysis ownerType, string optionKind, string role, string? partyKind )
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
                Typed( k, role, mine, partyKind );
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
            // A modeled section: its keys against the schema of its model (true is its defaults).
            if( t.ConfigurationSchema != null && (n.Kind == "Object" || n.Value == "true") )
            {
                var reserved = AllTypes.Where( o => o.OptionOwners.Contains( t.FullName ) && o.Configurable != null )
                                       .Select( o => o.Configurable!.Name )
                                       .ToHashSet( StringComparer.OrdinalIgnoreCase );
                bool isDefaults = n.Path.StartsWith( "Defaults:", StringComparison.OrdinalIgnoreCase ) || n.Path.Contains( ":Defaults:", StringComparison.OrdinalIgnoreCase );
                new SchemaChecker( Children, p => _byPath.GetValueOrDefault( p ), Error ).Check( n, t.ConfigurationSchema, t.Configurable.Name, reserved, checkRequired: !isDefaults );
            }
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

        // A scope where configurable types are declared: its levels, the nearest first (a declaration is looked up in each
        // level, the first one wins).
        sealed record Scope( IReadOnlyList<string> Levels );

        // The scopes of the owners of options in an AppContext: its AppContext aspects; for its App: the App aspects, the local
        // party and each of its clients.
        sealed record BuilderScopes( Scope BuilderAspects, Scope? AppAspects, Scope? Party, IReadOnlyList<(string Client, Scope Scope)> Clients );

        IEnumerable<BuilderScopes> GetScopes()
        {
            foreach( var b in Roles.Where( r => r.Value.Role == "AppContext" ).Select( r => r.Key ) )
            {
                var builderAspects = new Scope( [$"{b}:Aspects"] );
                var app = $"{b}:App";
                if( !_byPath.TryGetValue( app, out var appNode ) || appNode.Kind != "Object" )
                {
                    yield return new BuilderScopes( builderAspects, null, null, [] );
                    continue;
                }
                var appAspects = new Scope( [$"{app}:Aspects", "Defaults:App:Aspects"] );
                var party = new Scope( [$"{app}:Local", "Defaults:App:Local", $"{app}:Defaults", "Defaults:App:Defaults"] );
                var clients = party.Levels.SelectMany( l => Children( $"{l}:PartyClient" ) )
                                   .Where( c => Roles.TryGetValue( c.Path, out var r ) && r.Role == "Client" )
                                   .Select( c => c.Name )
                                   .Distinct( StringComparer.OrdinalIgnoreCase )
                                   .Select( c => (c, new Scope( party.Levels.Select( l => $"{l}:PartyClient:{c}" ).ToList() )) )
                                   .ToList();
                yield return new BuilderScopes( builderAspects, appAspects, party, clients );
            }
        }

        EditorNodeData? Lookup( Scope scope, string relative )
        {
            foreach( var level in scope.Levels )
            {
                if( _byPath.TryGetValue( $"{level}:{relative}", out var n ) ) return n;
            }
            return null;
        }

        static bool IsOff( EditorNodeData n ) => n.Kind == "Null" || (n.Kind == "Boolean" && n.Value == "false");

        // Active: a Required type always; otherwise its declaration (true or a section, not false) or, without
        // declaration, its activation (ByDefault).
        static bool IsActive( TypeAnalysis t, EditorNodeData? declaration )
            => t.Configurable!.Activation == "Required" || (declaration != null ? !IsOff( declaration ) : t.Configurable.Activation == "ByDefault");

        IEnumerable<TypeAnalysis> AllTypes => _catalog.Packages.SelectMany( p => p.Assemblies ).SelectMany( a => a.Types );

        void CheckRequirements()
        {
            foreach( var s in GetScopes() )
            {
                CheckOwners( s, s.BuilderAspects, "AppContextAspect" );
                if( s.AppAspects != null ) CheckOwners( s, s.AppAspects, "AppAspect" );
                if( s.Party != null ) CheckOwners( s, s.Party, "PartyFeature" );
                foreach( var (_, client) in s.Clients ) CheckOwners( s, client, "PartyClientFeature", client );
            }
        }

        // The owners of a kind declared in a scope (and not disabled) and their options.
        void CheckOwners( BuilderScopes scopes, Scope scope, string ownerKind, Scope? client = null )
        {
            var names = scope.Levels.SelectMany( Children ).Select( n => n.Name ).Distinct( StringComparer.OrdinalIgnoreCase );
            foreach( var name in names )
            {
                var declaration = Lookup( scope, name )!;
                if( IsOff( declaration ) ) continue;
                var ownerType = FindConfigurable( name, ownerKind );
                if( ownerType == null ) continue;
                var options = AllTypes.Where( t => t.Configurable?.Kind == ownerKind + "Option" && t.OptionOwners.Contains( ownerType.FullName ) ).ToList();
                if( options.Count == 0 ) continue;
                CheckOptionRequirements( scopes, scope, client, declaration, name, options );
            }
        }

        void CheckOptionRequirements( BuilderScopes scopes, Scope scope, Scope? client, EditorNodeData owner, string ownerName, List<TypeAnalysis> options )
        {
            var declarations = options.ToDictionary( o => o, o => Lookup( scope, $"{ownerName}:{o.Configurable!.Name}" ) );
            var enabled = options.Where( o => IsActive( o, declarations[o] ) ).ToHashSet();
            var reported = new HashSet<TypeAnalysis>();
            bool changed = true;
            while( changed )
            {
                changed = false;
                foreach( var o in options.Where( enabled.Contains ).ToList() )
                {
                    var missing = FindMissingRequirement( scopes, client, o, options, enabled );
                    if( missing == null ) continue;
                    var declaration = declarations[o];
                    var name = o.Configurable!.Name;
                    if( o.Configurable.Activation == "Required" || (declaration != null && !IsOff( declaration )) )
                    {
                        // The build fails: the option stays enabled for the others.
                        if( reported.Add( o ) ) Error( declaration?.Path ?? owner.Path, $"The option '{name}' requires {missing} that is disabled." );
                    }
                    else
                    {
                        enabled.Remove( o );
                        changed = true;
                        Problems.Add( new EditorProblemData( owner.Path, "Info", $"The option '{name}' (enabled by default) is disabled: it requires {missing} that is disabled." ) );
                    }
                }
            }
        }

        // Names the first non optional requirement of an option that is not met, null when they are all met. A requirement
        // that the catalog doesn't know (or that is not a configurable type) is ignored.
        string? FindMissingRequirement( BuilderScopes scopes, Scope? client, TypeAnalysis option, List<TypeAnalysis> options, HashSet<TypeAnalysis> enabled )
        {
            foreach( var r in option.Relations )
            {
                if( r.Kind != "Requires" || r.IsOptional ) continue;
                var target = _catalog.FindType( r.Target )?.Type;
                if( target?.Configurable == null ) continue;
                var name = target.Configurable.Name;
                if( options.Contains( target ) )
                {
                    if( !enabled.Contains( target ) ) return $"the option '{name}'";
                    continue;
                }
                var (scope, description) = target.Configurable.Kind switch
                {
                    "PartyFeature" => (scopes.Party, "the party feature"),
                    "PartyClientFeature" => (client, "the client feature"),
                    "AppAspect" => (scopes.AppAspects, "the App aspect"),
                    "AppContextAspect" => (scopes.BuilderAspects, "the AppContext aspect"),
                    _ => (null, "")
                };
                if( scope == null ) continue;
                if( !IsActive( target, Lookup( scope, name ) ) ) return $"{description} '{name}'";
            }
            return null;
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
                            Error( n.Path, "A \"$LOCK\" restricts the values of an App or of an AppContext aspect: it is outside them." );
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

        // The objects where a "$LOCK" can be (AppContextConfiguration): in an App ("AppContexts:{b}:App", "AppContexts:{b}:Apps",
        // "Defaults:App") or in an AppContext aspect ("AppContexts:{b}:Aspects:{aspect}", not "Aspects" itself).
        static bool IsLockable( string objectPath )
        {
            var s = objectPath.Split( ':' );
            if( s.Length >= 2 && s[0].Equals( "Defaults", StringComparison.OrdinalIgnoreCase ) && s[1].Equals( "App", StringComparison.OrdinalIgnoreCase ) ) return true;
            if( s.Length < 3 || !s[0].Equals( "AppContexts", StringComparison.OrdinalIgnoreCase ) ) return false;
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
