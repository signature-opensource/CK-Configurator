using CK.Configurator.Catalog;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfigurationCheckerTests
{
    static TypeAnalysis Configurable( string fullName, string kind, string name, string activation = "Optional", string[]? owners = null, string[]? secrets = null )
        => new()
        {
            FullName = fullName,
            CSharpName = fullName,
            Configurable = new ConfigurableType( kind, name, activation ),
            OptionOwners = owners ?? [],
            Secrets = secrets ?? []
        };

    static readonly CatalogModel _catalog = CatalogModel.Create( TestHelper.Monitor, null!,
    [
        new PackageAnalysis
        {
            PackageId = "Sample",
            Version = "1.0.0",
            Assemblies =
            [
                new AssemblyAnalysis
                {
                    Name = "Sample",
                    CentralKeys = [new CentralKey( "Domain", [] )],
                    Types =
                    [
                        Configurable( "Sample.SqlServerAspect", "AppBuilderAspect", "SqlServer" ),
                        Configurable( "Sample.GlobalizationAspect", "AppAspect", "Globalization" ),
                        Configurable( "Sample.HttpEndpoint", "PartyFeature", "HttpEndpoint" ),
                        Configurable( "Sample.InProcess", "PartyFeatureOption", "InProcess", owners: ["Sample.HttpEndpoint"] ),
                        Configurable( "Sample.AuthCenter", "PartyFeature", "AuthCenter" ),
                        Configurable( "Sample.Oidc", "PartyFeatureOption", "Oidc", owners: ["Sample.AuthCenter"], secrets: ["Party:*:ClientSecret"] ),
                        Configurable( "Sample.Core", "PartyFeature", "Core", activation: "Required" ),
                        Configurable( "Sample.PartyClient", "PartyFeature", "PartyClient" ),
                        Configurable( "Sample.NgApp", "PartyClientFeature", "NgApp" )
                    ]
                }
            ]
        }
    ] );

    static (EditorNodeData[] Nodes, EditorProblemData[] Problems) Check( string text )
    {
        var (nodes, problems) = ConfigurationChecker.Check( ConfiguratorEditorService.ReadNodes( text ), _catalog );
        return (nodes.ToArray(), problems.ToArray());
    }

    const string _app = "Builders:App/App.Builder:App";

    [Test]
    public void a_valid_document_is_typed_by_the_catalog()
    {
        var (nodes, problems) = Check( """
            {
              "$configurator": { "World": "w", "Version": "v" },
              "Domain": "CK",
              "Defaults": { "App": { "Logging": {} } },
              "Builders": {
                "App/App.Builder": {
                  "Aspects": { "SqlServer": { "Db": "x", "$LOCK": { "Db": "Compile" }, "$PER_DEPLOYMENT": [ "Db" ] } },
                  "App": {
                    "Aspects": { "Globalization": true },
                    "Local": {
                      "PublicUrl": "https://example.com",
                      "HttpEndpoint": { "InProcess": true },
                      "AuthCenter": { "Oidc": { "Google": { "ClientSecret": { "$SECRET": "Google:ClientSecret" } } } },
                      "Core": true,
                      "PartyClient": { "App": { "NgApp": true, "Types": [] } }
                    },
                    "CK-Monitoring": {}
                  }
                }
              }
            }
            """ );
        problems.ShouldBeEmpty();
        string? Role( string path ) => nodes.Single( n => n.Path == path ).Role;
        Role( "Builders:App/App.Builder" ).ShouldBe( "Builder" );
        Role( "Builders:App/App.Builder:Aspects:SqlServer" ).ShouldBe( "BuilderAspect" );
        Role( _app ).ShouldBe( "App" );
        Role( _app + ":Aspects:Globalization" ).ShouldBe( "AppAspect" );
        Role( _app + ":Local" ).ShouldBe( "Party" );
        Role( _app + ":Local:HttpEndpoint" ).ShouldBe( "PartyFeature" );
        Role( _app + ":Local:HttpEndpoint:InProcess" ).ShouldBe( "PartyFeatureOption" );
        Role( _app + ":Local:PartyClient:App" ).ShouldBe( "Client" );
        Role( _app + ":Local:PartyClient:App:NgApp" ).ShouldBe( "ClientFeature" );
        Role( _app + ":Local:PublicUrl" ).ShouldBeNull();
        Role( _app + ":CK-Monitoring" ).ShouldBeNull();
        nodes.Single( n => n.Path == _app + ":Local:AuthCenter:Oidc" ).TypeName.ShouldBe( "Oidc" );
    }

    [TestCase( """{ "Unknown": 1 }""", "Unknown", "Warning", "Unknown root key 'Unknown'" )]
    [TestCase( """{ "$configurator": { "World": "w" } }""", "$configurator", "Error", "\"$configurator\" must be" )]
    [TestCase( """{ "Builders": { "B": { "Apps2": [] } } }""", "Builders:B:Apps2", "Error", "Unknown key 'Apps2' in the builder 'B'" )]
    [TestCase( """{ "Builders": { "B": { "Aspects": { "Oracle": {} } } } }""", "Builders:B:Aspects:Oracle", "Warning", "'Oracle' is not a Builder aspect of the catalog." )]
    [TestCase( """{ "Builders": { "B": { "App": { "Aspects": { "SqlServer": {} } } } } }""", "Builders:B:App:Aspects:SqlServer", "Warning", "'SqlServer' is not an App aspect of the catalog." )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "Nope": true } } } } }""", "Builders:B:App:Local:Nope", "Warning", "'Nope' is not a party feature of the catalog" )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "Core": false } } } } }""", "Builders:B:App:Local:Core", "Error", "'Core' is required: it can't be disabled." )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "HttpEndpoint": "yes" } } } } }""", "Builders:B:App:Local:HttpEndpoint", "Error", "'HttpEndpoint' must be true, false or a section." )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "HttpEndpoint": { "Oidc": true } } } } } }""", "Builders:B:App:Local:HttpEndpoint:Oidc", "Warning", "'Oidc' is an option of 'AuthCenter', not of 'HttpEndpoint'." )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "AuthCenter": { "Oidc": { "G": { "ClientSecret": "s3cr3t" } } } } } } } }""", "Builders:B:App:Local:AuthCenter:Oidc:G:ClientSecret", "Warning", "'ClientSecret' is a secret" )]
    [TestCase( """{ "Builders": { "B": { "App": { "Local": { "PartyClient": { "App": { "Unknown": true } } } } } } }""", "Builders:B:App:Local:PartyClient:App:Unknown", "Warning", "'Unknown' is not a client feature of the catalog." )]
    [TestCase( """{ "Builders": { "B": { "Aspects": { "SqlServer": { "$LOCK": { "Db": "Later" } } } } } }""", "Builders:B:Aspects:SqlServer:$LOCK", "Error", "\"$LOCK\" must be an object" )]
    [TestCase( """{ "Builders": { "B": { "App": { "$PER_DEPLOYMENT": [ 1 ] } } } }""", "Builders:B:App:$PER_DEPLOYMENT", "Error", "\"$PER_DEPLOYMENT\" must be an array of paths" )]
    [TestCase( """{ "Builders": { "B": { "App": { "X": { "$SECRET": "k", "Other": 1 } } } } }""", "Builders:B:App:X:$SECRET", "Error", "must be the only key of its object" )]
    [TestCase( """{ "Defaults": { "Party": {} } }""", "Defaults:Party", "Warning", "Unknown key 'Party' in \"Defaults\"" )]
    public void problems( string text, string path, string severity, string message )
    {
        var (_, problems) = Check( text );
        var p = problems.ShouldHaveSingleItem();
        p.Path.ShouldBe( path );
        p.Severity.ShouldBe( severity );
        p.Message.ShouldContain( message );
    }
}
