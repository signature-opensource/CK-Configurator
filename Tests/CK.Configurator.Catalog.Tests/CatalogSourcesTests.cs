using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Catalog.Tests;

[TestFixture]
public class CatalogSourcesTests
{
    [TestCase( "https://github.com/signature-opensource/CK-Stack", "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/Published/index.json" )]
    [TestCase( "https://github.com/signature-opensource/CK-Stack.git/", "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/Published/index.json" )]
    [TestCase( "https://gitlab.com/signature-opensource/CK-Stack", null )]
    [TestCase( "https://github.com/signature-opensource", null )]
    public void the_raw_url_of_a_GitHub_Stack( string stackUrl, string? expected )
    {
        CatalogSources.GetRawUrl( stackUrl, "index.json" ).ShouldBe( expected );
    }

    [Test]
    public async Task a_source_from_a_local_Stack_Async()
    {
        var stack = Path.Combine( TestHelper.TestProjectFolder, "bin", "LocalStack" );
        var published = Path.Combine( stack, "Published" );
        if( Directory.Exists( stack ) ) Directory.Delete( stack, recursive: true );
        Directory.CreateDirectory( Path.Combine( published, "explo", "uc" ) );
        File.WriteAllText( Path.Combine( published, "index.json" ), """
            {
              "Alive": {
                "(stable)": [ "2026.10.9" ],
                "explo/uc-ci": [ "2026.11.22-0.uc.0.ci.1", "2026.11.22-0.uc.0.ci.0" ]
              },
              "Deprecated": {}
            }
            """ );
        File.WriteAllText( Path.Combine( published, "explo", "uc", "v2026.11.22-0.uc.0.ci.1.json" ), """
            {
              "StackUrl": "https://github.com/signature-opensource/CK-Stack",
              "World": "CK",
              "Version": "2026.11.22-0.uc.0.ci.1",
              "IsDeprecated": false,
              "Repositories": [
                { "Url": "https://github.com/signature-opensource/CK-AppIdentity", "Id": "19avtIQaSUE",
                  "Packages": [ "CK.AppIdentity.PartyClient@3.0.0-0.uc.0.ci.151" ] }
              ],
              "Feeds": [
                { "Name": "NuGet", "Url": "https://api.nuget.org/v3/index.json", "PushQualityFilter": "[papa,stable]" },
                { "Name": "GitHub", "Url": "https://nuget.pkg.github.com/signature-opensource/index.json",
                  "PublicReadCredentials": { "UserName": "reader", "Password": "not-a-secret" } }
              ]
            }
            """ );

        var source = (await CatalogSources.ResolveAsync( TestHelper.Monitor, stack, "explo/uc", isCI: true )).ShouldNotBeNull();
        source.Profile.Version.ToString().ShouldBe( "2026.11.22-0.uc.0.ci.1" );
        source.Feeds.Select( f => f.Name ).ShouldBe( ["NuGet", "GitHub"] );
        source.CatalogPackages.Single().PackageId.ShouldBe( "CK.AppIdentity.PartyClient" );

        // The worker's nuget.config has the feeds and the public read credentials.
        var worker = Path.Combine( stack, "Worker" );
        Directory.CreateDirectory( worker );
        CatalogWorker.WriteProject( worker, source, [], AnalyzerReference.FromPackage( "1.0.0" ) );
        var config = File.ReadAllText( Path.Combine( worker, "nuget.config" ) );
        config.ShouldContain( """<add key="GitHub" value="https://nuget.pkg.github.com/signature-opensource/index.json" />""" );
        config.ShouldContain( """<add key="ClearTextPassword" value="not-a-secret" />""" );

        // No regular profile for explo/uc, the stable one has no feeds (it predates them).
        (await CatalogSources.ResolveAsync( TestHelper.Monitor, stack, "explo/uc", isCI: false )).ShouldBeNull();
        (await CatalogSources.ResolveAsync( TestHelper.Monitor, stack, "stable", isCI: false )).ShouldBeNull();
    }
}
