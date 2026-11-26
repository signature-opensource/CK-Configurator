using CK.Core;
using CK.Testing;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfiguratorDeploymentTests
{
    const string _central = """
        {
          "Domain": "CK",
          "AppContexts": {
            "App": {
              "App": {
                "Local": {
                  // The endpoint.
                  "HttpEndpoint": { "Urls": "http://localhost:5000", "Limit": 10 },
                  "Demo": { "Password": "pass" }
                },
                "$PER_DEPLOYMENT": [ "Local:HttpEndpoint:Urls" ]
              }
            }
          }
        }
        """;

    const string _production = """
        {
          // The production.
          "AppContexts": {
            "App": {
              "App": {
                "Local": {
                  "HttpEndpoint": { "Urls": "https://*:443" },
                  "Demo": null
                }
              }
            }
          }
        }
        """;

    const string _staging = """
        {
          "$BASE": "#Production",
          "AppContexts": { "App": { "App": { "Local": { "HttpEndpoint": { "Urls": "https://staging:443" } } } } }
        }
        """;

    const string _local = "AppContexts:App:App:Local";

    static ConfiguratorEditorService CreateEditor() => new( new ConfiguratorCatalogService( new ConfigurationBuilder().Build() ) );

    static string CreateSolution( string name, params (string Name, string Text)[] deployments )
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "EditorDeployments", name );
        if( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
        Directory.CreateDirectory( folder );
        File.WriteAllText( Path.Combine( folder, "ckonf.json" ), _central );
        foreach( var (n, text) in deployments )
        {
            var d = Path.Combine( folder, "Deployment", n );
            Directory.CreateDirectory( d );
            File.WriteAllText( Path.Combine( d, "ckonf.json" ), text );
        }
        // Ignored folders.
        Directory.CreateDirectory( Path.Combine( folder, "Deployment", "Assets", "#Production" ) );
        Directory.CreateDirectory( Path.Combine( folder, "Deployment", ".build", "#X" ) );
        return folder;
    }

    [Test]
    public async Task the_view_of_a_deployment_is_its_merged_configuration_with_the_origins_Async()
    {
        var editor = CreateEditor();
        var s = await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "view", ("#Production", _production), ("#Staging", _staging) ) );
        s.View.ShouldBeNull();
        s.Documents.Select( d => (d.Name, d.Base) ).ShouldBe( [(null, null), ("#Production", null), ("#Staging", "#Production")] );
        // The central view has no origin and keeps its markers.
        s.Nodes.ShouldAllBe( n => n.Origin == null );
        s.Nodes.ShouldContain( n => n.Name == "$PER_DEPLOYMENT" );

        s = await editor.GetStateAsync( "#staging" );
        s.View.ShouldBe( "#Staging" );
        s.FilePath.ShouldEndWith( Path.Combine( "Deployment", "#Staging", "ckonf.json" ) );
        string? Origin( string path ) => s.Nodes.Single( n => n.Path == path ).Origin;
        Origin( "Domain" ).ShouldBe( "Central" );
        Origin( _local + ":HttpEndpoint:Limit" ).ShouldBe( "Central" );
        Origin( _local + ":HttpEndpoint:Urls" ).ShouldBe( "Deployment" );
        // An object that the deployment writes in is its own.
        Origin( _local + ":HttpEndpoint" ).ShouldBe( "Deployment" );
        Origin( "$BASE" ).ShouldBe( "Deployment" );
        // Removed by the base: not in the view.
        s.Nodes.ShouldNotContain( n => n.Name == "Demo" );
        // The markers of the central configuration are not in the view.
        s.Nodes.ShouldNotContain( n => n.Name == "$PER_DEPLOYMENT" );
        // The inherited comment.
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint" ).Comment.ShouldBe( "The endpoint." );

        s = await editor.GetStateAsync( "#Production" );
        s.Nodes.Single( n => n.Path == _local + ":Demo" ).Origin.ShouldBe( "Removed" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).Value.ShouldBe( "\"https://*:443\"" );
        // An unknown view is the central one (a deployment may disappear on a reload).
        (await editor.GetStateAsync( "#Unknown" )).View.ShouldBeNull();
    }

    [Test]
    public async Task the_edits_of_a_deployment_are_written_in_its_overlay_Async()
    {
        var folder = CreateSolution( "edits", ("#Production", _production) );
        var overlayFile = Path.Combine( folder, "Deployment", "#Production", "ckonf.json" );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );

        // An inherited value: the missing objects of its path are added to the overlay.
        var s = await editor.SetValueAsync( TestHelper.Monitor, "#Production", "Domain", "\"Other\"" );
        s.View.ShouldBe( "#Production" );
        s.Nodes.Single( n => n.Path == "Domain" ).Origin.ShouldBe( "Deployment" );
        s = await editor.SetValueAsync( TestHelper.Monitor, "#Production", _local + ":HttpEndpoint:Limit", "20" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Limit" ).ShouldSatisfyAllConditions( n => n.Value.ShouldBe( "20" ), n => n.Origin.ShouldBe( "Deployment" ) );
        s = await editor.AddPropertyAsync( TestHelper.Monitor, "#Production", "AppContexts:App:App", "Aspects", "{ \"Globalization\": true }" );
        s.Nodes.Single( n => n.Path == "AppContexts:App:App:Aspects:Globalization" ).Origin.ShouldBe( "Deployment" );
        await Should.ThrowAsync<CKException>( () => editor.AddPropertyAsync( TestHelper.Monitor, "#Production", "", "Domain", "1" ) );
        // The central document is unchanged.
        (await editor.GetStateAsync()).Nodes.Single( n => n.Path == "Domain" ).Value.ShouldBe( "\"CK\"" );

        // "Remove here": a null for an inherited property, a removal for one that only the overlay writes.
        s = await editor.RemovePropertyAsync( TestHelper.Monitor, "#Production", _local + ":HttpEndpoint:Limit" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Limit" ).Origin.ShouldBe( "Removed" );
        s = await editor.RemovePropertyAsync( TestHelper.Monitor, "#Production", "AppContexts:App:App:Aspects" );
        s.Nodes.ShouldNotContain( n => n.Name == "Aspects" );

        // Revert: the inherited value again; the objects that become empty "{}" are removed when an object is inherited.
        s = await editor.RevertPropertyAsync( TestHelper.Monitor, "#Production", _local + ":HttpEndpoint:Limit" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Limit" ).ShouldSatisfyAllConditions( n => n.Value.ShouldBe( "10" ), n => n.Origin.ShouldBe( "Central" ) );
        s = await editor.RevertPropertyAsync( TestHelper.Monitor, "#Production", _local + ":HttpEndpoint:Urls" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint" ).Origin.ShouldBe( "Central" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).Value.ShouldBe( "\"http://localhost:5000\"" );
        await Should.ThrowAsync<CKException>( () => editor.RevertPropertyAsync( TestHelper.Monitor, "#Production", "AppContexts:App:App:Local:HttpEndpoint:Urls" ) );
        await Should.ThrowAsync<CKException>( () => editor.RevertPropertyAsync( TestHelper.Monitor, null, "Domain" ) );

        s = await editor.SaveAsync( TestHelper.Monitor, "#Production", overwrite: false );
        s.IsDirty.ShouldBeFalse();
        var saved = File.ReadAllText( overlayFile );
        saved.ShouldContain( "// The production." );
        saved.ShouldContain( "\"Domain\": \"Other\"" );
        // "HttpEndpoint" became empty and is inherited: it is removed; "Local" keeps "Demo": null.
        saved.ShouldNotContain( "HttpEndpoint" );
        saved.ShouldContain( "\"Demo\": null" );
        File.ReadAllText( Path.Combine( folder, "ckonf.json" ) ).ShouldBe( _central );
    }

    [Test]
    public async Task undo_spans_the_documents_and_shows_the_changed_one_Async()
    {
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "undo", ("#Production", _production) ) );
        await editor.SetValueAsync( TestHelper.Monitor, null, "Domain", "\"Central\"" );
        await editor.SetValueAsync( TestHelper.Monitor, "#Production", "Domain", "\"Prod\"" );
        var s = await editor.UndoAsync();
        s.View.ShouldBe( "#Production" );
        s.Nodes.Single( n => n.Path == "Domain" ).ShouldSatisfyAllConditions( n => n.Value.ShouldBe( "\"Central\"" ), n => n.Origin.ShouldBe( "Central" ) );
        s = await editor.UndoAsync();
        s.View.ShouldBeNull();
        s.Nodes.Single( n => n.Path == "Domain" ).Value.ShouldBe( "\"CK\"" );
        s.IsDirty.ShouldBeFalse();
        s = await editor.RedoAsync();
        s.View.ShouldBeNull();
        s = await editor.RedoAsync();
        s.View.ShouldBe( "#Production" );
        s.Documents.Single( d => d.Name == "#Production" ).IsDirty.ShouldBeTrue();
    }

    [Test]
    public async Task creating_a_deployment_Async()
    {
        var folder = CreateSolution( "create", ("#Production", _production) );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );
        (await Should.ThrowAsync<CKException>( () => editor.CreateDeploymentAsync( TestHelper.Monitor, "#Dev", null ) )).Message.ShouldContain( "the development" );
        (await Should.ThrowAsync<CKException>( () => editor.CreateDeploymentAsync( TestHelper.Monitor, "#production", null ) )).Message.ShouldContain( "already exists" );
        (await Should.ThrowAsync<CKException>( () => editor.CreateDeploymentAsync( TestHelper.Monitor, "#Staging", "#Nope" ) )).Message.ShouldContain( "doesn't exist" );

        var s = await editor.CreateDeploymentAsync( TestHelper.Monitor, "Paris/#Staging", "#production" );
        s.View.ShouldBe( "Paris/#Staging" );
        s.Documents.Select( d => d.Name ).ShouldBe( [null, "#Production", "Paris/#Staging"] );
        s.Documents.Single( d => d.Name == "Paris/#Staging" ).ShouldSatisfyAllConditions( d => d.IsNew.ShouldBeTrue(), d => d.Base.ShouldBe( "#Production" ) );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).Origin.ShouldBe( "#Production" );
        s.Nodes.Single( n => n.Path == "Domain" ).Origin.ShouldBe( "Central" );

        // Undone, then redone.
        s = await editor.UndoAsync();
        s.Documents.Count.ShouldBe( 2 );
        s.View.ShouldBeNull();
        s = await editor.RedoAsync();
        s.View.ShouldBe( "Paris/#Staging" );

        // An edit in the new (empty) overlay, then the save writes its file.
        s = await editor.SetValueAsync( TestHelper.Monitor, "Paris/#Staging", _local + ":HttpEndpoint:Urls", "\"https://paris:443\"" );
        s = await editor.SaveAsync( TestHelper.Monitor, null, overwrite: false );
        s.Documents.ShouldAllBe( d => !d.IsNew && !d.IsDirty );
        var text = File.ReadAllText( Path.Combine( folder, "Deployment", "Paris", "#Staging", "ckonf.json" ) );
        text.ShouldContain( "// The \"Paris/#Staging\" deployment: an overlay of the \"#Production\" deployment" );
        text.ShouldContain( "\"$BASE\": \"#Production\"" );
        text.ShouldContain( "\"https://paris:443\"" );

        // It is found again.
        s = await editor.ReloadAsync( TestHelper.Monitor, "Paris/#Staging" );
        s.View.ShouldBe( "Paris/#Staging" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).Origin.ShouldBe( "Deployment" );
    }

    [Test]
    public async Task the_static_checks_of_a_deployment_Async()
    {
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "checks",
                                                                     ("#Production", _production),
                                                                     ("#Staging", """{ "$BASE": "#Production", "Domain": "X", "Deployment": { "AppContexts": [ "App", "Nope" ] } }"""),
                                                                     ("#A", """{ "$BASE": "#B" }"""),
                                                                     ("#B", """{ "$BASE": "#A" }""") ) );
        // #Production writes the value per deployment of the central marker.
        var s = await editor.GetStateAsync( "#Production" );
        s.Problems.ShouldNotContain( p => p.Severity == "Error" );
        // #Staging inherits it from #Production: an error.
        s = await editor.GetStateAsync( "#Staging" );
        s.Problems.ShouldContain( p => p.Path == _local + ":HttpEndpoint:Urls" && p.Message.Contains( "inherits \"AppContexts:App:App:Local:HttpEndpoint:Urls\"" ) );
        s.Problems.ShouldContain( p => p.Path == "Domain" && p.Message.Contains( "can't be written in a deployment" ) );
        s.Problems.ShouldContain( p => p.Path == "Deployment:AppContexts" && p.Message.Contains( "Invalid AppContext 'Nope'" ) );
        s.Documents.Single( d => d.Name == "#Staging" ).Errors.ShouldBe( 3 );
        s = await editor.GetStateAsync( "#A" );
        s.Problems.ShouldContain( p => p.Path == "$BASE" && p.Message.Contains( "overlay each other" ) );
    }

    [Test]
    public async Task an_inherited_value_per_deployment_is_one_problem_per_written_property_and_can_be_written_here_Async()
    {
        var folder = CreateSolution( "write-here", ("#Production", """{ "Ports": [ 80, 443 ] }""") );
        // A marker on an array: its items are written as a whole.
        File.WriteAllText( Path.Combine( folder, "ckonf.json" ), _central.Replace( "\"Domain\": \"CK\",", "\"Domain\": \"CK\", \"Ports\": [ 1, 2 ], \"$PER_DEPLOYMENT\": [ \"Ports\" ]," ) );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );
        var s = await editor.CreateDeploymentAsync( TestHelper.Monitor, "#Staging", "#Production" );
        var inherited = s.Problems.Where( p => p.Message.Contains( "inherits" ) ).ToList();
        inherited.Select( p => p.Path ).ShouldBe( ["Ports", _local + ":HttpEndpoint:Urls"], ignoreOrder: true );
        inherited.ShouldAllBe( p => p.Fix == EditorProblemData.WriteHereFix );
        inherited.Single( p => p.Path == "Ports" ).Message.ShouldContain( "inherits \"Ports\"" );

        // The fix: the inherited value of the property, written in the overlay.
        foreach( var p in inherited )
        {
            s = await editor.SetValueAsync( TestHelper.Monitor, "#Staging", p.Path, s.Nodes.Single( n => n.Path == p.Path ).Value! );
        }
        s.Problems.ShouldNotContain( p => p.Message.Contains( "inherits" ) );
        s.Nodes.Single( n => n.Path == "Ports" ).ShouldSatisfyAllConditions( n => n.Value.ShouldBe( "[ 80, 443 ]" ), n => n.Origin.ShouldBe( "Deployment" ) );
    }

    [Test]
    public async Task a_new_deployment_can_write_its_values_per_deployment_Async()
    {
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "create-values", ("#Production", _production) ) );
        var s = await editor.CreateDeploymentAsync( TestHelper.Monitor, "#Staging", "#Production", writeValuesPerDeployment: true );
        s.Problems.ShouldNotContain( p => p.Message.Contains( "inherits" ) );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).ShouldSatisfyAllConditions( n => n.Value.ShouldBe( "\"https://*:443\"" ),
                                                                                                    n => n.Origin.ShouldBe( "Deployment" ) );
        // The rest is inherited.
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Limit" ).Origin.ShouldBe( "Central" );
        // One edit: undone, the deployment disappears.
        s = await editor.UndoAsync();
        s.Documents.ShouldNotContain( d => d.Name == "#Staging" );
    }
}
