using CK.Core;
using CK.Testing;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfiguratorEditorServiceTests
{
    const string _central = """
        {
          "$configurator": { "World": "https://github.com/signature-opensource/CK-Stack", "Version": "2026.11.24-0.uc.0.ci.0" },
          // The domain of the parties.
          "Domain": "CK",
          "Builders": {
            "App/App.Builder": {
              "App": {
                "Local": {
                  /* The endpoint. */
                  "HttpEndpoint": { "InProcess": true },
                  "Ports": [ 1, 2 ],
                  "Limit": -1.5,
                  "Nothing": null
                }
              }
            }
          }
        }
        """;

    const string _local = "Builders:App/App.Builder:App:Local";

    static ConfiguratorEditorService CreateEditor() => new( new ConfiguratorCatalogService( new ConfigurationBuilder().Build() ) );

    static string CreateSolution( string name, string text, bool bom = false )
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "Editor", name );
        Directory.CreateDirectory( folder );
        var bytes = Encoding.UTF8.GetBytes( text );
        if( bom ) bytes = Encoding.UTF8.GetPreamble().Concat( bytes ).ToArray();
        File.WriteAllBytes( Path.Combine( folder, "ckonf.json" ), bytes );
        return folder;
    }

    [Test]
    public async Task opening_a_solution_gives_its_properties_comments_and_catalog_Async()
    {
        var editor = CreateEditor();
        (await editor.GetStateAsync()).ShouldBe( EditorSnapshot.Closed );
        var s = await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "open", _central ) );
        s.CatalogWorld.ShouldBe( "https://github.com/signature-opensource/CK-Stack" );
        s.CatalogVersion.ShouldBe( "2026.11.24-0.uc.0.ci.0" );
        s.IsDirty.ShouldBeFalse();
        s.ChangedOnDisk.ShouldBeFalse();
        // Its catalog is not opened: nothing is checked.
        s.Problems.ShouldHaveSingleItem().Message.ShouldContain( "is not opened: open it to check the document" );
        var domain = s.Nodes.Single( n => n.Path == "Domain" );
        domain.ShouldBe( new EditorNodeData( "Domain", "Domain", 0, "String", "\"CK\"", "The domain of the parties." ) );
        var endpoint = s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint" );
        endpoint.Depth.ShouldBe( 4 );
        endpoint.Kind.ShouldBe( "Object" );
        endpoint.Value.ShouldBeNull();
        endpoint.Comment.ShouldBe( "The endpoint." );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:InProcess" ).Kind.ShouldBe( "Boolean" );
        s.Nodes.Single( n => n.Path == _local + ":Ports" ).ShouldBe( new EditorNodeData( _local + ":Ports", "Ports", 4, "Array", "[ 1, 2 ]", null ) );
        s.Nodes.Single( n => n.Path == _local + ":Limit" ).Kind.ShouldBe( "Number" );
        s.Nodes.Single( n => n.Path == _local + ":Nothing" ).Kind.ShouldBe( "Null" );
        // Document order: a property is followed by the ones of its value.
        s.Nodes.Select( n => n.Name ).Take( 6 ).ShouldBe( ["$configurator", "World", "Version", "Domain", "Builders", "App/App.Builder"] );
    }

    [Test]
    public async Task edits_are_undone_and_redone_and_saved_with_the_comments_and_the_BOM_Async()
    {
        var folder = CreateSolution( "edits", _central, bom: true );
        var file = Path.Combine( folder, "ckonf.json" );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );

        // The names are case insensitive.
        var s = await editor.SetValueAsync( TestHelper.Monitor, "domain", "\"Other\"" );
        s.IsDirty.ShouldBeTrue();
        s.CanUndo.ShouldBeTrue();
        s.Nodes.Single( n => n.Path == "Domain" ).Value.ShouldBe( "\"Other\"" );
        s = await editor.AddPropertyAsync( TestHelper.Monitor, _local, "PartyClient", "{ \"App\": { \"NgApp\": true } }" );
        s.Nodes.Single( n => n.Path == _local + ":PartyClient:App:NgApp" ).Value.ShouldBe( "true" );
        s = await editor.RemovePropertyAsync( TestHelper.Monitor, _local + ":Nothing" );
        s.Nodes.ShouldNotContain( n => n.Name == "Nothing" );

        s = await editor.UndoAsync();
        s.Nodes.ShouldContain( n => n.Name == "Nothing" );
        s.CanRedo.ShouldBeTrue();
        s = await editor.RedoAsync();
        s.Nodes.ShouldNotContain( n => n.Name == "Nothing" );
        s.CanRedo.ShouldBeFalse();

        s = await editor.SaveAsync( TestHelper.Monitor, overwrite: false );
        s.IsDirty.ShouldBeFalse();
        var bytes = File.ReadAllBytes( file );
        bytes.AsSpan().StartsWith( Encoding.UTF8.GetPreamble() ).ShouldBeTrue();
        var saved = Encoding.UTF8.GetString( bytes.AsSpan( 3 ) );
        saved.ShouldContain( "// The domain of the parties.\n  \"Domain\": \"Other\"," );
        saved.ShouldContain( "/* The endpoint. */" );
        saved.ShouldNotContain( "Nothing" );

        // Undoing back to the saved text: not dirty.
        s = await editor.UndoAsync();
        s.IsDirty.ShouldBeTrue();
        s = await editor.RedoAsync();
        s.IsDirty.ShouldBeFalse();
    }

    [Test]
    public async Task a_change_on_disk_is_detected_and_the_save_must_overwrite_or_reload_Async()
    {
        var folder = CreateSolution( "disk", _central );
        var file = Path.Combine( folder, "ckonf.json" );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );
        await editor.SetValueAsync( TestHelper.Monitor, "Domain", "\"Mine\"" );

        File.WriteAllText( file, _central.Replace( "\"CK\"", "\"Theirs\"" ) );
        (await editor.GetStateAsync()).ChangedOnDisk.ShouldBeTrue();
        var ex = await Should.ThrowAsync<CKException>( () => editor.SaveAsync( TestHelper.Monitor, overwrite: false ) );
        ex.Message.ShouldContain( "changed on disk since it was read" );
        File.ReadAllText( file ).ShouldContain( "\"Theirs\"" );

        var s = await editor.ReloadAsync( TestHelper.Monitor );
        s.ChangedOnDisk.ShouldBeFalse();
        s.IsDirty.ShouldBeFalse();
        s.CanUndo.ShouldBeFalse();
        s.Nodes.Single( n => n.Path == "Domain" ).Value.ShouldBe( "\"Theirs\"" );

        await editor.SetValueAsync( TestHelper.Monitor, "Domain", "\"Mine\"" );
        File.WriteAllText( file, _central );
        s = await editor.SaveAsync( TestHelper.Monitor, overwrite: true );
        s.ChangedOnDisk.ShouldBeFalse();
        File.ReadAllText( file ).ShouldContain( "\"Mine\"" );
    }

    [Test]
    public async Task errors_Async()
    {
        var editor = CreateEditor();
        (await Should.ThrowAsync<CKException>( () => editor.UndoAsync() )).Message.ShouldBe( "No solution is opened." );
        var empty = Path.Combine( TestHelper.TestProjectFolder, "bin", "Editor", "empty" );
        Directory.CreateDirectory( empty );
        (await Should.ThrowAsync<CKException>( () => editor.OpenAsync( TestHelper.Monitor, empty ) )).Message.ShouldContain( "has no 'ckonf.json'" );
        var invalid = CreateSolution( "invalid", "{ \"a\": 1, " );
        (await Should.ThrowAsync<CKException>( () => editor.OpenAsync( TestHelper.Monitor, invalid ) )).Message.ShouldContain( "is not a valid JSON(C) document" );

        await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "errors", _central ) );
        await Should.ThrowAsync<CKException>( () => editor.SetValueAsync( TestHelper.Monitor, "Missing", "1" ) );
        await Should.ThrowAsync<CKException>( () => editor.AddPropertyAsync( TestHelper.Monitor, "", "Domain", "1" ) );
        await Should.ThrowAsync<CKException>( () => editor.SetValueAsync( TestHelper.Monitor, "Domain", "{ oops" ) );
        var s = await editor.GetStateAsync();
        s.IsDirty.ShouldBeFalse();
        s.CanUndo.ShouldBeFalse();
        (await editor.CloseAsync()).ShouldBe( EditorSnapshot.Closed );
    }
}
