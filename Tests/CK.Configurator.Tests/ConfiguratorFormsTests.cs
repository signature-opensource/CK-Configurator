using CK.Configurator.Catalog;
using CK.Core;
using CK.Testing;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfiguratorFormsTests
{
    // As CK.ConfigurationModel's ConfigurationModelSchema writes it.
    const string _schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "Url": { "type": "string", "format": "uri", "description": "The url." },
            "Timeout": { "type": "string", "x-ck-format": "timespan", "default": "00:05:00" },
            "Mode": { "type": "string", "enum": [ "Off", "Lax", "Strict" ] },
            "Retries": { "type": "integer", "x-ck-level": "Run" },
            "Enabled": { "type": "boolean" },
            "Ports": { "type": "array", "items": { "type": "integer" } },
            "Cultures": { "anyOf": [ { "type": "array", "items": { "type": "string" } }, { "type": "string" } ] },
            "Limits": { "$ref": "#/$defs/Limits" },
            "Schemes": { "type": "object", "additionalProperties": { "$ref": "#/$defs/Scheme" } },
            "Lifetimes": { "type": "object", "additionalProperties": { "type": "string", "x-ck-format": "timespan" } },
            "Kestrel": { "$ref": "https://json.schemastore.org/appsettings.json#/definitions/kestrel" },
            "Password": { "type": "string", "x-ck-secret": true }
          },
          "required": [ "Url" ],
          "additionalProperties": false,
          "$defs": {
            "Limits": { "type": "object", "properties": { "Max": { "type": "integer" } }, "additionalProperties": false },
            "Scheme": { "type": "object", "properties": { "ClientId": { "type": "string" } }, "required": [ "ClientId" ], "additionalProperties": false }
          }
        }
        """;

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
                    Types =
                    [
                        new TypeAnalysis
                        {
                            FullName = "Sample.Endpoint",
                            CSharpName = "Endpoint",
                            Configurable = new ConfigurableType( "PartyFeature", "Endpoint", "Optional" ),
                            ConfigurationSchema = (JsonObject)JsonNode.Parse( _schema )!
                        },
                        // A feature with a compile time model only ("build time" keys), one without any model, and one
                        // with a key read on the party's own section only.
                        new TypeAnalysis
                        {
                            FullName = "Sample.Build",
                            CSharpName = "Build",
                            Configurable = new ConfigurableType( "PartyFeature", "Build", "Optional" ),
                            CompileConfigurationSchema = (JsonObject)JsonNode.Parse( """{ "type": "object", "properties": { "Port": { "type": "integer" } }, "additionalProperties": false }""" )!
                        },
                        new TypeAnalysis
                        {
                            FullName = "Sample.Bare",
                            CSharpName = "Bare",
                            Configurable = new ConfigurableType( "PartyFeature", "Bare", "Optional" )
                        },
                        new TypeAnalysis
                        {
                            FullName = "Sample.Keys",
                            CSharpName = "Keys",
                            Configurable = new ConfigurableType( "PartyFeature", "Keys", "Optional" ),
                            ConfigurationSchema = (JsonObject)JsonNode.Parse( """{ "type": "object", "properties": { "Anchors": { "type": "string", "x-ck-noDefaults": true }, "Days": { "type": "integer" } }, "additionalProperties": false }""" )!
                        },
                        new TypeAnalysis
                        {
                            FullName = "Sample.Opt",
                            CSharpName = "Opt",
                            Configurable = new ConfigurableType( "PartyFeatureOption", "Opt", "Optional" ),
                            OptionOwners = ["Sample.Endpoint"]
                        }
                    ]
                }
            ]
        }
    ] );

    const string _local = "AppContexts:B:App:Local";

    static EditorProblemData[] Check( string local, string defaults = "{}" )
    {
        var text = $$"""
            {
              "Defaults": { "App": { "Defaults": {{defaults}} } },
              "AppContexts": { "B": { "App": { "Local": {{local}} } } }
            }
            """;
        return ConfigurationChecker.Check( ConfiguratorEditorService.ReadNodes( text ), _catalog ).Problems.ToArray();
    }

    [Test]
    public void a_valid_section_has_no_problem()
    {
        Check( """
            { "Endpoint": {
                "Url": "https://example.com", "Timeout": "1.00:00:00", "Mode": "lax", "Retries": 3, "Enabled": true, "Ports": [ 80, 443 ],
                "Cultures": "fr,en", "Limits": { "Max": 10 }, "Schemes": { "Google": { "ClientId": "x" } },
                "Kestrel": { "Anything": { "Goes": 1 } }, "Password": { "$SECRET": "Endpoint:Password" }, "Opt": true } }
            """ ).ShouldBeEmpty();
        // The required "Url" from the "Defaults" of the party: key by key.
        Check( """{ "Endpoint": { "Mode": "Off" } }""", defaults: """{ "Endpoint": { "Url": "https://default" } }""" ).ShouldBeEmpty();
        // A "Defaults" section is partial: its required keys are checked where it is used.
        Check( "{}", defaults: """{ "Endpoint": { "Mode": "Off" } }""" ).ShouldBeEmpty();
    }

    [TestCase( """{ "Endpoint": { "Url": "x", "Nope": 1 } }""", _local + ":Endpoint:Nope", "Unknown key 'Nope' in 'Endpoint'" )]
    [TestCase( """{ "Endpoint": { "Mode": "Off" } }""", _local + ":Endpoint", "'Url' is required in 'Endpoint'." )]
    [TestCase( """{ "Endpoint": true }""", _local + ":Endpoint", "'Url' is required in 'Endpoint'." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Timeout": "5 minutes" } }""", _local + ":Endpoint:Timeout", "is a time span" )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Mode": "Loose" } }""", _local + ":Endpoint:Mode", "is one of 'Off', 'Lax', 'Strict'." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Retries": 1.5 } }""", _local + ":Endpoint:Retries", "is an integer." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Enabled": "yes" } }""", _local + ":Endpoint:Enabled", "is true or false." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Ports": [ "80" ] } }""", _local + ":Endpoint:Ports", "is a list of integer." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Cultures": 12 } }""", _local + ":Endpoint:Cultures", "is a list of strings" )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Limits": { "Max": 1, "Min": 0 } } }""", _local + ":Endpoint:Limits:Min", "Unknown key 'Min'" )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Limits": 3 } }""", _local + ":Endpoint:Limits", "is a section." )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Schemes": { "G": {} } } }""", _local + ":Endpoint:Schemes:G", "'ClientId' is required" )]
    [TestCase( """{ "Endpoint": { "Url": "x", "Lifetimes": { "A": "00:10:00", "B": "soon" } } }""", _local + ":Endpoint:Lifetimes:B", "is a time span" )]
    public void the_schema_problems( string local, string path, string message )
    {
        var p = Check( local ).ShouldHaveSingleItem();
        p.Path.ShouldBe( path );
        p.Severity.ShouldBe( "Error" );
        p.Message.ShouldContain( message );
    }

    [Test]
    public void the_absent_keys_take_their_value_from_the_Defaults_key_by_key()
    {
        var text = """
            {
              "Defaults": { "App": { "Defaults": { "Endpoint": { "Mode": "Strict", "Limits": { "Max": 5 } } } } },
              "AppContexts": { "B": { "App": { "Local": { "Endpoint": { "Url": "x" } }, "Defaults": { "Endpoint": { "Retries": 2 } } } } }
            }
            """;
        var (typed, _) = ConfigurationChecker.Check( ConfiguratorEditorService.ReadNodes( text ), _catalog );
        var values = EditorDefaults.Compute( typed, _catalog ).Where( v => v.Path.StartsWith( _local ) ).ToList();
        values.ShouldContain( new EditorDefaultedValueData( _local + ":Endpoint:Retries", "2", "AppContexts:B:App:Defaults:Endpoint:Retries" ) );
        values.ShouldContain( new EditorDefaultedValueData( _local + ":Endpoint:Mode", "\"Strict\"", "Defaults:App:Defaults:Endpoint:Mode" ) );
        // The keys of a nested model are followed.
        values.ShouldContain( new EditorDefaultedValueData( _local + ":Endpoint:Limits:Max", "5", "Defaults:App:Defaults:Endpoint:Limits:Max" ) );
        values.ShouldNotContain( v => v.Path == _local + ":Endpoint:Url" );
    }

    [Test]
    public async Task a_key_of_a_model_is_written_with_its_path_Async()
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "EditorForms" );
        if( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
        Directory.CreateDirectory( folder );
        File.WriteAllText( Path.Combine( folder, "ckonf.json" ), """
            {
              "AppContexts": { "B": { "App": { "Local": { "Endpoint": true } } } }
            }
            """ );
        var editor = new ConfiguratorEditorService( new ConfiguratorCatalogService( new ConfigurationBuilder().Build() ) );
        await editor.OpenAsync( TestHelper.Monitor, folder );
        // Without CreatePath: an existing property only.
        await Should.ThrowAsync<CKException>( () => editor.SetValueAsync( TestHelper.Monitor, null, _local + ":Endpoint:Url", "\"x\"" ) );
        // "true" becomes the section of the key.
        var s = await editor.SetValueAsync( TestHelper.Monitor, null, _local + ":Endpoint:Url", "\"https://x\"", createPath: true );
        s.Nodes.Single( n => n.Path == _local + ":Endpoint:Url" ).Value.ShouldBe( "\"https://x\"" );
        // The missing objects of a path.
        s = await editor.SetValueAsync( TestHelper.Monitor, null, _local + ":Endpoint:Limits:Max", "10", createPath: true );
        s.Nodes.Single( n => n.Path == _local + ":Endpoint:Limits:Max" ).Value.ShouldBe( "10" );
        // An existing value.
        s = await editor.SetValueAsync( TestHelper.Monitor, null, _local + ":Endpoint:Limits:Max", "20", createPath: true );
        s.Nodes.Single( n => n.Path == _local + ":Endpoint:Limits:Max" ).Value.ShouldBe( "20" );
        // A value is not a section.
        (await Should.ThrowAsync<CKException>( () => editor.SetValueAsync( TestHelper.Monitor, null, _local + ":Endpoint:Url:Sub", "1", createPath: true ) ))
            .Message.ShouldContain( "is not a section" );
    }

    [Test]
    public void the_compile_time_keys_are_checked_against_their_schema()
    {
        Check( """{ "Build": { "Port": 4200 } }""" ).ShouldBeEmpty();
        var p = Check( """{ "Build": { "Port": "x" } }""" ).ShouldHaveSingleItem();
        p.Path.ShouldBe( _local + ":Build:Port" );
        p.Message.ShouldContain( "is an integer." );
        Check( """{ "Build": { "Nope": 1 } }""" ).ShouldContain( e => e.Path == _local + ":Build:Nope" && e.Message.Contains( "Unknown key 'Nope'" ) );
    }

    [Test]
    public void a_feature_without_model_has_no_configuration()
    {
        Check( """{ "Bare": true }""" ).ShouldBeEmpty();
        var p = Check( """{ "Bare": { "Size": 3 } }""" ).ShouldHaveSingleItem();
        p.Path.ShouldBe( _local + ":Bare:Size" );
        p.Message.ShouldContain( "'Bare' has no configuration of its own" );
    }

    [Test]
    public void a_NoDefaults_key_can_not_be_set_in_a_Defaults()
    {
        Check( """{ "Keys": { "Anchors": "a", "Days": 3 } }""" ).ShouldBeEmpty();
        Check( "{}", defaults: """{ "Keys": { "Days": 3 } }""" ).ShouldBeEmpty();
        var p = Check( "{}", defaults: """{ "Keys": { "Anchors": "a" } }""" ).ShouldHaveSingleItem();
        p.Path.ShouldEndWith( "Defaults:Keys:Anchors" );
        p.Message.ShouldContain( "can't be set in a \"Defaults\"" );
    }
}
