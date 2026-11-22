using CK.Core;
using CK.Packaging.Abstractions;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Catalog.Tests;

[TestFixture]
public class CatalogTests
{
    // A subset of the explo/uc CI profile of 2026-11-22: packages that declare configurable types, configuration
    // declarations, client facts and commands.
    const string _profile = """
        {
          "StackUrl": "https://github.com/signature-opensource/CK-Stack",
          "World": "CK",
          "Version": "2026.11.22-0.uc.0.ci.0",
          "IsDeprecated": false,
          "Repositories": [
            { "Url": "https://github.com/signature-opensource/CK-AppIdentity", "Id": "19avtIQaSUE",
              "Packages": [ "CK.AppIdentity.PartyClient@3.0.0-0.uc.0.ci.151" ] },
            { "Url": "https://github.com/signature-opensource/CK-AppIdentity-Transport", "Id": "rzheU9-ntBE",
              "Packages": [ "CK.AppIdentity.TransportLayer@4.0.0-0.uc.0.ci.60" ] },
            { "Url": "https://github.com/signature-opensource/CK-AspNet", "Id": "R-8R-4qqyVc",
              "Packages": [ "CK.AppIdentity.HttpEndpoint@25.0.0-0.uc.0.ci.49" ] },
            { "Url": "https://github.com/signature-opensource/CK-Cris-Abstractions", "Id": "NuqgNBQFZL0",
              "Packages": [ "CK.IO.DynamicConfiguration@3.0.0-0.uc.0.ci.105" ] }
          ]
        }
        """;

    static CatalogSource CreateSource() => new CatalogSource( PublishedProfile.Parse( Encoding.UTF8.GetBytes( _profile ) ),
                                                              [
                                                                  new CatalogFeed( "NuGet", "https://api.nuget.org/v3/index.json" ),
                                                                  new CatalogFeed( "Signature-OpenSource", "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" )
                                                              ] );

    [TestCase( "CK.Testing.Nunit", false )]
    [TestCase( "CK.Sample.Tests", false )]
    [TestCase( "CK.AppBuilder", false )]
    [TestCase( "CK.AppBuilder.Contracts", true )]
    [TestCase( "CK.AppIdentity", true )]
    public void the_catalog_packages( string packageId, bool expected )
    {
        CatalogSource.IsCatalogPackage( packageId ).ShouldBe( expected );
    }

    [Test]
    public void an_analysis_round_trips_and_skips_the_empty_lists()
    {
        var a = new PackageAnalysis
        {
            PackageId = "CK.Sample",
            Version = "1.0.0",
            Assemblies =
            [
                new AssemblyAnalysis
                {
                    Name = "CK.Sample",
                    Role = AssemblyRole.Model,
                    RequiredEngines = [new RequiredEngine( "CK.Sample.Engine", null )],
                    Types =
                    [
                        new TypeAnalysis
                        {
                            FullName = "CK.Sample.SampleFeature",
                            CSharpName = "CK.Sample.SampleFeature",
                            ResourceGroup = ResourceGroupKind.Package,
                            Configurable = new ConfigurableType( "PartyFeature", "Sample", "ByDefault" ),
                            PerDeployment = ["Party:Address"],
                            Relations = [new TypeRelation( "Requires", "CK.Other.OtherFeature", true )]
                        }
                    ]
                }
            ]
        };
        var json = Encoding.UTF8.GetString( CatalogJson.Write( a ) );
        json.ShouldNotContain( "\"Locks\"" );
        json.ShouldNotContain( "\"Section\"" );
        json.ShouldContain( "\"Role\": \"Model\"" );
        json.ShouldContain( "\"Activation\": \"ByDefault\"" );

        var back = CatalogJson.Read( Encoding.UTF8.GetBytes( json ) );
        Encoding.UTF8.GetString( CatalogJson.Write( back ) ).ShouldBe( json );
        back.Assemblies[0].Types[0].Locks.ShouldBeEmpty();
        back.Assemblies[0].Types[0].Relations.Single().ShouldBe( new TypeRelation( "Requires", "CK.Other.OtherFeature", true ) );
    }

    [Test]
    public void the_cache_keys_by_package_version_and_format()
    {
        var cache = new CatalogCache( Path.Combine( TestHelper.TestProjectFolder, "bin", "CacheTest" ) );
        var path = cache.GetAnalysisPath( "CK.Sample", "1.0.0-Beta" );
        path.ShouldEndWith( Path.Combine( "packages", "ck.sample", $"1.0.0-beta.v{PackageAnalysis.Format}.json" ) );

        if( File.Exists( path ) ) File.Delete( path );
        cache.TryLoad( TestHelper.Monitor, "CK.Sample", "1.0.0-Beta" ).ShouldBeNull();
        cache.Save( new PackageAnalysis { PackageId = "CK.Sample", Version = "1.0.0-Beta" } );
        cache.TryLoad( TestHelper.Monitor, "ck.sample", "1.0.0-beta" ).ShouldNotBeNull().PackageId.ShouldBe( "CK.Sample" );

        File.WriteAllText( path, "not json" );
        cache.TryLoad( TestHelper.Monitor, "CK.Sample", "1.0.0-Beta" ).ShouldBeNull();
        File.Exists( path ).ShouldBeFalse();
    }

    /// <summary>
    /// Restores the packages from the public feeds, builds the worker (with the analyzer project) and runs it.
    /// </summary>
    [Test]
    public async Task analyzing_a_source_Async()
    {
        var bin = Path.Combine( TestHelper.TestProjectFolder, "bin" );
        var cacheRoot = Path.Combine( bin, "Cache" );
        if( Directory.Exists( cacheRoot ) ) Directory.Delete( cacheRoot, recursive: true );
        var cache = new CatalogCache( cacheRoot );
        var source = CreateSource();
        var analyzer = AnalyzerReference.FromProject( Path.Combine( TestHelper.SolutionFolder,
                                                                    "CK.Configurator.Catalog.Worker",
                                                                    "CK.Configurator.Catalog.Worker.csproj" ) );

        (await CatalogWorker.AnalyzeAsync( TestHelper.Monitor, source, cache, Path.Combine( bin, "Worker" ), analyzer )).ShouldBeTrue();

        var catalog = Catalog.Load( TestHelper.Monitor, source, cache ).ShouldNotBeNull();
        catalog.Packages.Count.ShouldBe( 4 );

        // The transport and the HTTP endpoint are party features with per deployment values.
        var transport = catalog.FindConfigurable( "Transport" ).ShouldHaveSingleItem();
        transport.Package.PackageId.ShouldBe( "CK.AppIdentity.TransportLayer" );
        transport.Type.Configurable!.Kind.ShouldBe( "PartyFeature" );
        transport.Type.PerDeployment.ShouldContain( "Party:ListeningAddress" );

        var http = catalog.FindConfigurable( "HttpEndpoint" ).ShouldHaveSingleItem();
        http.Type.PerDeployment.ShouldContain( "Party:Urls" );

        // The DynamicConfiguration commands are declared (their families are the catalog's work).
        var dynamic = catalog.Packages.Single( p => p.PackageId == "CK.IO.DynamicConfiguration" );
        dynamic.Assemblies.SelectMany( a => a.Types ).Where( t => t.Command != null ).ShouldNotBeEmpty();

        // A second run has nothing to do.
        (await CatalogWorker.AnalyzeAsync( TestHelper.Monitor, source, cache, Path.Combine( bin, "Worker" ), analyzer )).ShouldBeTrue();
    }

    /// <summary>
    /// Analyzes a whole published profile: its path is in the CK_CATALOG_PROFILE environment variable (a profile of
    /// the Published folder of a Stack). The analyses go to the default cache.
    /// </summary>
    [Test]
    [Explicit]
    public async Task analyzing_a_whole_profile_Async()
    {
        var profilePath = System.Environment.GetEnvironmentVariable( "CK_CATALOG_PROFILE" );
        Assume.That( profilePath, Is.Not.Null, "The CK_CATALOG_PROFILE environment variable is not set." );
        var profile = PublishedProfile.Parse( File.ReadAllBytes( profilePath! ) );
        var source = new CatalogSource( profile, CreateSource().Feeds );
        var cache = new CatalogCache( CatalogCache.DefaultRoot );
        var analyzer = AnalyzerReference.FromProject( Path.Combine( TestHelper.SolutionFolder,
                                                                    "CK.Configurator.Catalog.Worker",
                                                                    "CK.Configurator.Catalog.Worker.csproj" ) );
        var worker = Path.Combine( CatalogCache.DefaultRoot, "sources", profile.Version.ToString() );
        (await CatalogWorker.AnalyzeAsync( TestHelper.Monitor, source, cache, worker, analyzer )).ShouldBeTrue();
        var catalog = Catalog.Load( TestHelper.Monitor, source, cache ).ShouldNotBeNull();
        TestHelper.Monitor.Info( $"{catalog.Packages.Count} packages, configurable types: '{catalog.ConfigurableNames.Order().Concatenate( "', '" )}'." );
        var byExposure = catalog.Commands.Values.GroupBy( c => c.Exposure ).Select( g => $"{g.Key}: {g.Count()}" );
        TestHelper.Monitor.Info( $"{catalog.Commands.Count} commands ({byExposure.Concatenate()}), "
                                 + $"{catalog.Commands.Values.Count( c => c.Interfaces.Select( i => i.Package ).Distinct().Count() > 1 )} extended by another package." );
        // An [Internal] command of CK.AppIdentity.Cris (when the profile has it).
        if( catalog.FindCommandByInterface( "CK.AppIdentity.Cris.IGetKeyEventsQCommand" ) is { } keyEvents )
        {
            keyEvents.Exposure.ShouldBe( "Internal" );
        }
    }
}
