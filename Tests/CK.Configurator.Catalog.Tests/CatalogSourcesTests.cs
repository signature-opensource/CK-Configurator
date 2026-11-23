using CK.Core;
using CK.Packaging.Abstractions;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Catalog.Tests;

[TestFixture]
public class CatalogSourcesTests
{
    [TestCase( "https://github.com/signature-opensource/CK-Stack",
               "https://github.com/signature-opensource/CK-Stack",
               "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/Published/index.json" )]
    [TestCase( "https://github.com/signature-opensource/CK-Stack.git/",
               "https://github.com/signature-opensource/CK-Stack",
               "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/Published/index.json" )]
    [TestCase( "https://github.com/signature-opensource/CK-Stack/tree/main/@net8",
               "https://github.com/signature-opensource/CK-Stack/tree/main/@net8",
               "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/@net8/Published/index.json" )]
    [TestCase( "https://github.com/signature-opensource/CK-Stack/tree/main/net8", null, null )]
    [TestCase( "https://github.com/signature-opensource/CK-Stack/tree/develop/@net8", null, null )]
    [TestCase( "https://gitlab.com/signature-opensource/CK-Stack", null, null )]
    [TestCase( "https://github.com/signature-opensource", null, null )]
    public void a_World_on_GitHub( string world, string? canonical, string? indexUrl )
    {
        if( canonical == null )
        {
            CatalogWorld.TryParse( world, out _ ).ShouldBeFalse();
            return;
        }
        var w = CatalogWorld.Parse( world );
        w.ToString().ShouldBe( canonical );
        w.IsLocal.ShouldBeFalse();
        w.GetLocation( "index.json" ).ShouldBe( indexUrl );
        CatalogWorld.Parse( w.ToString() ).ShouldBe( w );
    }

    [Test]
    public void a_local_World()
    {
        var stack = Path.GetFullPath( Path.Combine( TestHelper.TestProjectFolder, "bin", "WorldStack" ) );
        Directory.CreateDirectory( Path.Combine( stack, "@net8" ) );
        var w = CatalogWorld.Parse( stack );
        w.IsLocal.ShouldBeTrue();
        w.LTSName.ShouldBeNull();
        w.CachePath.ShouldBeNull();
        w.GetLocation( "explo/uc/v2026.11.23-0.uc.json" ).ShouldBe( Path.Combine( stack, "Published", "explo", "uc", "v2026.11.23-0.uc.json" ) );

        var lts = CatalogWorld.Parse( Path.Combine( stack, "@net8" ) + Path.DirectorySeparatorChar );
        lts.Stack.ShouldBe( stack );
        lts.LTSName.ShouldBe( "@net8" );
        lts.GetLocation( "index.json" ).ShouldBe( Path.Combine( stack, "@net8", "Published", "index.json" ) );

        CatalogWorld.TryParse( Path.Combine( stack, "Nope" ), out _ ).ShouldBeFalse();
    }

    const string _feeds = """
        ,
        "Feeds": [
          { "Name": "NuGet", "Url": "https://api.nuget.org/v3/index.json", "PushQualityFilter": "[papa,stable]" },
          { "Name": "GitHub", "Url": "https://nuget.pkg.github.com/signature-opensource/index.json",
            "PublicReadCredentials": { "UserName": "reader", "Password": "not-a-secret" } }
        ]
        """;

    static string Profile( string version, bool withFeeds ) => $$"""
        {
          "StackUrl": "https://github.com/signature-opensource/CK-Stack",
          "World": "CK",
          "Version": "{{version}}",
          "IsDeprecated": false,
          "Repositories": [
            { "Url": "https://github.com/signature-opensource/CK-AppIdentity", "Id": "19avtIQaSUE",
              "Packages": [ "CK.AppIdentity.PartyClient@3.0.0-0.uc.0.ci.151" ] }
          ]{{(withFeeds ? _feeds : "")}}
        }
        """;

    // The Published folder of a World, as path -> content.
    static readonly Dictionary<string, string> _published = new()
    {
        ["index.json"] = """
            {
              "Alive": {
                "(stable)": [ "2026.10.9" ],
                "explo/uc": [ "2026.11.17-0.uc" ],
                "explo/uc-ci": [ "2026.11.23-0.uc.0.ci.0", "2026.11.22.1-0.uc.0.ci.0" ]
              },
              "Deprecated": {
                "explo/uc-ci": [ "2026.11.22-0.uc.0.ci.0" ],
                "explo/old": [ "2026.9.1-0.old" ]
              },
              "LTSWorlds": [ "@net8" ]
            }
            """,
        ["v2026.10.9.json"] = Profile( "2026.10.9", withFeeds: false ),
        ["explo/uc/v2026.11.17-0.uc.json"] = Profile( "2026.11.17-0.uc", withFeeds: false ),
        ["explo/uc/v2026.11.23-0.uc.0.ci.0.json"] = Profile( "2026.11.23-0.uc.0.ci.0", withFeeds: true ),
        ["explo/uc/v2026.11.22.1-0.uc.0.ci.0.json"] = Profile( "2026.11.22.1-0.uc.0.ci.0", withFeeds: true ),
        ["explo/uc/v2026.11.22-0.uc.0.ci.0.json"] = Profile( "2026.11.22-0.uc.0.ci.0", withFeeds: true ),
        // A profile published before the TimeVersion: its file has its legacy name (2026.9.1 is the 244th day).
        ["explo/old/v2026.244.0-0.old.json"] = Profile( "2026.244.0-0.old", withFeeds: true ),
    };

    static string CreateLocalStack()
    {
        var stack = Path.Combine( TestHelper.TestProjectFolder, "bin", "LocalStack" );
        if( Directory.Exists( stack ) ) Directory.Delete( stack, recursive: true );
        foreach( var (path, content) in _published )
        {
            var file = Path.Combine( stack, "Published", path );
            Directory.CreateDirectory( Path.GetDirectoryName( file )! );
            File.WriteAllText( file, content );
        }
        return stack;
    }

    static async Task CheckListsAsync( CatalogSources sources, CatalogWorld world )
    {
        var branches = (await sources.GetBranchesAsync( TestHelper.Monitor, world )).ShouldNotBeNull();
        branches.Select( b => $"{b.Name}: {b.Latest.Version}" ).ShouldBe( ["stable: 2026.10.9", "explo/uc: 2026.11.23-0.uc.0.ci.0"] );
        branches[0].Latest.HasFeeds.ShouldBeFalse();
        branches[1].Latest.HasFeeds.ShouldBeTrue();

        branches = (await sources.GetBranchesAsync( TestHelper.Monitor, world, includeDeprecated: true )).ShouldNotBeNull();
        branches.Select( b => $"{b.Name}: {b.Latest.Version}{(b.Latest.IsDeprecated ? " (deprecated)" : "")}" )
                .ShouldBe( ["stable: 2026.10.9", "explo/old: 2026.9.1-0.old (deprecated)", "explo/uc: 2026.11.23-0.uc.0.ci.0"] );
        // The legacy profile is read (by its legacy file name).
        branches[1].Latest.HasFeeds.ShouldBeTrue();

        // The regular and CI builds together, the latest first, the deprecated ones hidden by default.
        var profiles = (await sources.GetProfilesAsync( TestHelper.Monitor, world, "explo/uc" )).ShouldNotBeNull();
        profiles.Select( p => $"{p.Version}{(p.Version.IsCI ? " CI" : "")}{(p.HasFeeds ? "" : " (no feeds)")}" )
                .ShouldBe( ["2026.11.23-0.uc.0.ci.0 CI", "2026.11.22.1-0.uc.0.ci.0 CI", "2026.11.17-0.uc (no feeds)"] );
        profiles = (await sources.GetProfilesAsync( TestHelper.Monitor, world, "explo/uc", includeDeprecated: true )).ShouldNotBeNull();
        profiles.Select( p => p.Version.ToString() )
                .ShouldBe( ["2026.11.23-0.uc.0.ci.0", "2026.11.22.1-0.uc.0.ci.0", "2026.11.22-0.uc.0.ci.0", "2026.11.17-0.uc"] );
        profiles.Single( p => p.IsDeprecated ).Version.ToString().ShouldBe( "2026.11.22-0.uc.0.ci.0" );

        (await sources.GetProfilesAsync( TestHelper.Monitor, world, "stable" )).ShouldNotBeNull().Single().Version.ToString().ShouldBe( "2026.10.9" );
        (await sources.GetProfilesAsync( TestHelper.Monitor, world, "" )).ShouldNotBeNull().Count.ShouldBe( 1 );
        (await sources.GetProfilesAsync( TestHelper.Monitor, world, "alpha" )).ShouldNotBeNull().ShouldBeEmpty();
    }

    [Test]
    public async Task a_local_World_lists_its_branches_and_profiles_Async()
    {
        var stack = CreateLocalStack();
        var world = CatalogWorld.Parse( stack );
        using var sources = new CatalogSources();
        await CheckListsAsync( sources, world );

        // The Worlds of the Stack: its default World, then the LTS Worlds that its index names.
        var worlds = (await sources.GetWorldsAsync( TestHelper.Monitor, world )).ShouldNotBeNull();
        worlds.Select( w => w.LTSName ).ShouldBe( [null, "@net8"] );
        worlds[1].ToString().ShouldBe( Path.Combine( world.Stack, "@net8" ) );
        worlds[1].DefaultWorld.ShouldBe( world );

        var source = (await sources.ResolveAsync( TestHelper.Monitor, world, TimeVersion.Parse( "2026.11.23-0.uc.0.ci.0" ) )).ShouldNotBeNull();
        source.Profile.Version.ToString().ShouldBe( "2026.11.23-0.uc.0.ci.0" );
        source.Feeds.Select( f => f.Name ).ShouldBe( ["NuGet", "GitHub"] );
        source.CatalogPackages.Single().PackageId.ShouldBe( "CK.AppIdentity.PartyClient" );

        // The worker's nuget.config has the feeds and the public read credentials.
        var worker = Path.Combine( stack, "Worker" );
        Directory.CreateDirectory( worker );
        CatalogWorker.WriteProject( worker, source, [], AnalyzerReference.FromPackage( "1.0.0" ) );
        var config = File.ReadAllText( Path.Combine( worker, "nuget.config" ) );
        config.ShouldContain( """<add key="GitHub" value="https://nuget.pkg.github.com/signature-opensource/index.json" />""" );
        config.ShouldContain( """<add key="ClearTextPassword" value="not-a-secret" />""" );

        // The profile without feeds can't be a source (unless feeds are given), an unknown version neither.
        var noFeeds = TimeVersion.Parse( "2026.11.17-0.uc" );
        (await sources.ResolveAsync( TestHelper.Monitor, world, noFeeds )).ShouldBeNull();
        (await sources.ResolveAsync( TestHelper.Monitor, world, noFeeds, source.Feeds )).ShouldNotBeNull();
        (await sources.ResolveAsync( TestHelper.Monitor, world, TimeVersion.Parse( "2026.11.24-0.uc" ) )).ShouldBeNull();
    }

    sealed class FakeGitHub : HttpMessageHandler
    {
        public readonly List<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage request, CancellationToken cancellationToken )
        {
            var url = request.RequestUri!.ToString();
            Requests.Add( url );
            const string prefix = "https://raw.githubusercontent.com/signature-opensource/CK-Stack/main/Published/";
            var r = url.StartsWith( prefix ) && _published.TryGetValue( url[prefix.Length..], out var content )
                        ? new HttpResponseMessage( HttpStatusCode.OK ) { Content = new StringContent( content, Encoding.UTF8 ) }
                        : new HttpResponseMessage( HttpStatusCode.NotFound );
            return Task.FromResult( r );
        }
    }

    [Test]
    public async Task the_profiles_of_a_remote_World_are_read_once_Async()
    {
        var cache = Path.Combine( TestHelper.TestProjectFolder, "bin", "ProfileCache" );
        if( Directory.Exists( cache ) ) Directory.Delete( cache, recursive: true );
        var world = CatalogWorld.Parse( "https://github.com/signature-opensource/CK-Stack" );
        world.CachePath.ShouldBe( "github.com/signature-opensource/ck-stack" );

        var github = new FakeGitHub();
        using( var sources = new CatalogSources( new HttpClient( github ), cache ) )
        {
            await CheckListsAsync( sources, world );
        }
        // The index is read each time, a profile once (the legacy one is first tried by its TimeVersion name).
        github.Requests.Where( r => !r.EndsWith( "index.json" ) ).Count().ShouldBe( _published.Count );
        File.Exists( Path.Combine( cache, "github.com", "signature-opensource", "ck-stack", "explo", "uc", "v2026.11.23-0.uc.0.ci.0.json" ) ).ShouldBeTrue();

        // Another reader on the same cache folder reads the profiles from it.
        github.Requests.Clear();
        using( var sources = new CatalogSources( new HttpClient( github ), cache ) )
        {
            await CheckListsAsync( sources, world );
        }
        github.Requests.ShouldAllBe( r => r.EndsWith( "index.json" ) );
    }

    [Test]
    [Explicit( "Reads the GitHub CK-Stack." )]
    public async Task the_GitHub_CK_Stack_Async()
    {
        using var sources = new CatalogSources();
        var world = CatalogWorld.Parse( "https://github.com/signature-opensource/CK-Stack" );
        var branches = (await sources.GetBranchesAsync( TestHelper.Monitor, world, includeDeprecated: true )).ShouldNotBeNull();
        foreach( var b in branches )
        {
            var profiles = (await sources.GetProfilesAsync( TestHelper.Monitor, world, b.Name, includeDeprecated: true )).ShouldNotBeNull();
            TestHelper.Monitor.Info( $"{b.Name} - {b.Latest.Version}: "
                                     + profiles.Select( p => $"{p.Version}{(p.IsDeprecated ? " (deprecated)" : "")}{(p.HasFeeds ? "" : " (no feeds)")}" ).Concatenate() );
        }
        branches.ShouldContain( b => b.Name == "explo/uc" && b.Latest.HasFeeds );
        // The September profiles of CK@net8 predate the TimeVersion: their files have their legacy name, they have no feeds.
        var net8 = (await sources.GetBranchesAsync( TestHelper.Monitor, CatalogWorld.Parse( "https://github.com/signature-opensource/CK-Stack/tree/main/@net8" ) ))
                    .ShouldNotBeNull().ShouldHaveSingleItem();
        net8.Name.ShouldBe( "stable" );
        net8.Latest.Version.ToString().ShouldBe( "2026.9.23" );
        net8.Latest.HasFeeds.ShouldBeFalse();
    }
}
