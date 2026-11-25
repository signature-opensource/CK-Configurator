using CK.Configurator.Catalog;
using CK.Core;
using CK.Testing;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfiguratorLocksAndSecretsTests
{
    const string _central = """
        {
          "Domain": "CK",
          "Defaults": { "App": { "$LOCK": { "Local:Demo": "Init" } } },
          "Builders": {
            "App/App.Builder": {
              "App": {
                "Local": {
                  "HttpEndpoint": { "Urls": "http://localhost:5000", "Limit": 10 },
                  "Oidc": { "ClientSecret": "s3cr3t" },
                  "Demo": { "Password": "pass" }
                },
                "Defaults": { "$LOCK": { "Store": "Compile" }, "Store": "x" }
              }
            }
          }
        }
        """;

    const string _app = "Builders:App/App.Builder:App";
    const string _local = _app + ":Local";

    static ConfiguratorEditorService CreateEditor() => new( new ConfiguratorCatalogService( new ConfigurationBuilder().Build() ) );

    static string CreateSolution( string name, string? production = null )
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "EditorLocks", name );
        if( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
        Directory.CreateDirectory( folder );
        File.WriteAllText( Path.Combine( folder, "ckonf.json" ), _central );
        if( production != null )
        {
            Directory.CreateDirectory( Path.Combine( folder, "Deployment", "#Production" ) );
            File.WriteAllText( Path.Combine( folder, "Deployment", "#Production", "ckonf.json" ), production );
        }
        return folder;
    }

    [Test]
    public async Task the_locks_of_the_LOCK_entries_and_their_fallbacks_Async()
    {
        var editor = CreateEditor();
        var s = await editor.OpenAsync( TestHelper.Monitor, CreateSolution( "locks-view" ) );
        // "Defaults:App" applies to every App; the "Defaults" of an App to its local party too.
        s.Nodes.Single( n => n.Path == _local + ":Demo:Password" ).ShouldSatisfyAllConditions( n => n.LockLevel.ShouldBe( "Init" ),
                                                                                             n => n.CanUnlock.ShouldBeFalse() );
        s.Nodes.Single( n => n.Path == _app + ":Defaults:Store" ).ShouldSatisfyAllConditions( n => n.LockLevel.ShouldBe( "Compile" ),
                                                                                            n => n.CanUnlock.ShouldBeTrue() );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).LockLevel.ShouldBeNull();
        // The entries of a "$LOCK" are not locked values.
        s.Nodes.Where( n => n.Path.Contains( "$LOCK" ) ).ShouldAllBe( n => n.LockLevel == null );
    }

    [Test]
    public async Task lock_and_unlock_a_property_Async()
    {
        var folder = CreateSolution( "lock", production: """{ "Builders": { "App/App.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "https://*:443" } } } } } }""" );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );

        // In the "$LOCK" of the App (created), the path relative to it as the pattern.
        var s = await editor.LockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint", "Init" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint" ).ShouldSatisfyAllConditions( n => n.LockLevel.ShouldBe( "Init" ), n => n.CanUnlock.ShouldBeTrue() );
        // A pattern covers what is below it.
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).ShouldSatisfyAllConditions( n => n.LockLevel.ShouldBe( "Init" ), n => n.CanUnlock.ShouldBeFalse() );
        // Another entry of the same "$LOCK", then a new level for an existing one; the lowest level wins.
        s = await editor.LockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint:Urls", "Compile" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).ShouldSatisfyAllConditions( n => n.LockLevel.ShouldBe( "Compile" ), n => n.CanUnlock.ShouldBeTrue() );
        s = await editor.LockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint", "Run" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Limit" ).LockLevel.ShouldBe( "Run" );

        // The view of a deployment shows the locks; they are edited in the central configuration.
        var p = await editor.GetStateAsync( "#Production" );
        p.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).LockLevel.ShouldBe( "Compile" );
        await Should.ThrowAsync<CKException>( () => editor.LockPropertyAsync( TestHelper.Monitor, "#Production", _local + ":HttpEndpoint", "Init" ) );
        // Outside an App or a Builder aspect.
        (await Should.ThrowAsync<CKException>( () => editor.LockPropertyAsync( TestHelper.Monitor, null, "Domain", "Compile" ) )).Message.ShouldContain( "can't be locked" );
        await Should.ThrowAsync<CKException>( () => editor.LockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint", "Later" ) );
        // Only a literal pattern at the path of the property is unlocked.
        (await Should.ThrowAsync<CKException>( () => editor.UnlockPropertyAsync( TestHelper.Monitor, null, _local + ":Demo:Password" ) )).Message.ShouldContain( "\"$LOCK\" of 'Defaults:App'" );

        s = await editor.UnlockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint:Urls" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).LockLevel.ShouldBe( "Run" );
        s = await editor.UnlockPropertyAsync( TestHelper.Monitor, null, _local + ":HttpEndpoint" );
        s.Nodes.Single( n => n.Path == _local + ":HttpEndpoint:Urls" ).LockLevel.ShouldBeNull();
        // The "$LOCK" that became empty is removed: back to the file as it was.
        await editor.SaveAsync( TestHelper.Monitor, null, overwrite: false );
        File.ReadAllText( Path.Combine( folder, "ckonf.json" ) ).ShouldBe( _central );
    }

    [Test]
    public async Task a_value_becomes_a_secret_in_the_development_secrets_Async()
    {
        var folder = CreateSolution( "secret" );
        var secretsFile = Path.Combine( folder, "#Dev", "Secrets.json" );
        var editor = CreateEditor();
        var s = await editor.OpenAsync( TestHelper.Monitor, folder );
        s.Nodes.Single( n => n.Path == _local + ":Oidc:ClientSecret" ).SecretName.ShouldBeNull();

        (await Should.ThrowAsync<CKException>( () => editor.MakeSecretAsync( TestHelper.Monitor, null, _local + ":Oidc:ClientSecret", "Bad Name", false, false ) ))
            .Message.ShouldContain( "Invalid secret name" );
        s = await editor.MakeSecretAsync( TestHelper.Monitor, null, _local + ":Oidc:ClientSecret", "Oidc:ClientSecret", false, false );
        s.IsDirty.ShouldBeTrue();
        s.Nodes.Single( n => n.Path == _local + ":Oidc:ClientSecret" ).ShouldSatisfyAllConditions( n => n.Kind.ShouldBe( "Object" ),
                                                                                                 n => n.SecretName.ShouldBe( "Oidc:ClientSecret" ),
                                                                                                 n => n.SecretStatus.ShouldBe( "Set" ) );
        // Another value under the same name: refused unless overwritten.
        (await Should.ThrowAsync<CKException>( () => editor.MakeSecretAsync( TestHelper.Monitor, null, _local + ":Demo:Password", "Oidc:ClientSecret", false, false ) ))
            .Message.ShouldContain( "already exists with another value" );
        s = await editor.MakeSecretAsync( TestHelper.Monitor, null, _local + ":Demo:Password", "Demo:Password", isOptional: true, overwrite: false );
        s.Nodes.Single( n => n.Path == _local + ":Demo:Password" ).SecretStatus.ShouldBe( "Set" );

        s = await editor.SaveAsync( TestHelper.Monitor, null, overwrite: false );
        s.IsDirty.ShouldBeFalse();
        var central = File.ReadAllText( Path.Combine( folder, "ckonf.json" ) );
        central.ShouldContain( "\"ClientSecret\": { \"$SECRET\": \"Oidc:ClientSecret\" }" );
        central.ShouldContain( "\"Password\": { \"$SECRET?\": \"Demo:Password\" }" );
        central.ShouldNotContain( "s3cr3t" );
        var secrets = File.ReadAllText( secretsFile );
        secrets.ShouldContain( "\"s3cr3t\"" );
        secrets.ShouldContain( "\"pass\"" );
        CK.Core.ConfigurationSecret.TryReadFile( secretsFile, out var values, out var error ).ShouldBeTrue( error );
        values["Oidc:ClientSecret"].ShouldBe( "s3cr3t" );
        values["Demo:Password"].ShouldBe( "pass" );

        // One undo restores the two documents.
        s = await editor.UndoAsync();
        s.Nodes.Single( n => n.Path == _local + ":Demo:Password" ).Value.ShouldBe( "\"pass\"" );
        s.IsDirty.ShouldBeTrue();
        s = await editor.RedoAsync();
        s.IsDirty.ShouldBeFalse();

        // A secret that the file doesn't have is "Missing".
        File.WriteAllText( secretsFile, "{}" );
        s = await editor.ReloadAsync( TestHelper.Monitor, null );
        s.Nodes.Single( n => n.Path == _local + ":Oidc:ClientSecret" ).SecretStatus.ShouldBe( "Missing" );
        s.Nodes.Single( n => n.Path == _local + ":Demo:Password" ).SecretStatus.ShouldBe( "MissingOptional" );
    }

    [Test]
    public async Task in_a_deployment_only_the_handle_is_written_Async()
    {
        var folder = CreateSolution( "secret-deployment", production: "{}" );
        var editor = CreateEditor();
        await editor.OpenAsync( TestHelper.Monitor, folder );
        var s = await editor.MakeSecretAsync( TestHelper.Monitor, "#Production", _local + ":Oidc:ClientSecret", "Prod:Oidc", false, false );
        s.Nodes.Single( n => n.Path == _local + ":Oidc:ClientSecret" ).ShouldSatisfyAllConditions( n => n.SecretName.ShouldBe( "Prod:Oidc" ),
                                                                                                 n => n.SecretStatus.ShouldBeNull(),
                                                                                                 n => n.Origin.ShouldBe( "Deployment" ) );
        await editor.SaveAsync( TestHelper.Monitor, null, overwrite: false );
        File.ReadAllText( Path.Combine( folder, "Deployment", "#Production", "ckonf.json" ) ).ShouldContain( "{ \"$SECRET\": \"Prod:Oidc\" }" );
        File.Exists( Path.Combine( folder, "#Dev", "Secrets.json" ) ).ShouldBeFalse();
        File.ReadAllText( Path.Combine( folder, "ckonf.json" ) ).ShouldBe( _central );
    }

    [Test]
    public void the_locks_of_the_packages_and_the_capabilities_come_from_the_catalog()
    {
        var catalog = CatalogModel.Create( TestHelper.Monitor, null!,
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
                                FullName = "Sample.HttpEndpoint",
                                CSharpName = "HttpEndpoint",
                                Configurable = new ConfigurableType( "PartyFeature", "HttpEndpoint", "Optional" ),
                                Locks = [new ConfigurationLock( "Party:Kestrel", "Compile", null )],
                                PropertyLevels = [new PropertyLevel( "Urls", "Init" )]
                            }
                        ]
                    }
                ]
            }
        ] );
        var text = """{ "Builders": { "B": { "App": { "Local": { "HttpEndpoint": { "Urls": "x", "Kestrel": { "A": 1 } } } } } } }""";
        var (typed, _) = ConfigurationChecker.Check( ConfiguratorEditorService.ReadNodes( text ), catalog );
        var nodes = EditorAnnotations.Annotate( typed, catalog, null, out _ );
        var kestrel = nodes.Single( n => n.Path == "Builders:B:App:Local:HttpEndpoint:Kestrel:A" );
        kestrel.LockLevel.ShouldBe( "Compile" );
        kestrel.LockSource.ShouldBe( "[ConfigurationLock( \"Party:Kestrel\" )] of 'HttpEndpoint'" );
        kestrel.CanUnlock.ShouldBeFalse();
        nodes.Single( n => n.Path == "Builders:B:App:Local:HttpEndpoint:Urls" ).ShouldSatisfyAllConditions( n => n.MaxLevel.ShouldBe( "Init" ),
                                                                                                          n => n.LockLevel.ShouldBeNull() );
        nodes.Single( n => n.Path == "Builders:B:App:Local:HttpEndpoint:Kestrel" ).MaxLevel.ShouldBeNull();
    }
}
