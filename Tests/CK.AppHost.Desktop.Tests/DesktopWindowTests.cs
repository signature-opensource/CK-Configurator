using CK.AppIdentity;
using CK.Core;
using CK.Setup;
using CK.Testing;
using CK.TypeScript;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Desktop.Tests;

[TestFixture]
public class DesktopWindowTests
{
    /// <summary>
    /// A web build ("TestBuild") that stands for "NgApp" (CK.TS.Angular): the client application of the ClientDirectory.
    /// </summary>
    [PartyClientFeature( "TestBuild", TypeActivation.Optional )]
    public sealed class TestBuildClientFeature : ClientFeature, IWebAppBuild
    {
        public TestBuildClientFeature( PartyClient client, ImmutableConfigurationSection configuration, ClientInfo info )
            : base( client, configuration )
        {
            Info = info;
        }

        public ClientInfo Info { get; }

        public NormalizedPath ProjectPath => Info.ProjectPath;

        public NormalizedPath OutputPath => Info.OutputPath;

        public System.Diagnostics.ProcessStartInfo GetDevServerStart( string yarnPath, int port ) => new( "node", $"\"{yarnPath}\" ng serve --port {port}" );
    }

    public sealed class TestBuildClientFeatureDriver : ClientFeatureDriver<TestBuildClientFeature>
    {
        readonly ClientDirectory _directory;

        public TestBuildClientFeatureDriver( ClientDirectory directory ) => _directory = directory;

        protected override TestBuildClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
            => new( client, configuration, _directory.Clients.Single( c => c.Name == client.Name ) );
    }

    static IStObjMap? _map;

    // The App (served at "/") and the MiniApp (served at "/mini-app"), both built: the host runs in Production.
    static async Task<IStObjMap> GetMapAsync()
    {
        if( _map == null )
        {
            var root = TestHelper.CleanupFolder( TestHelper.GetTypeScriptGeneratedOnlyTargetProjectPath(), false );
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Path = TestHelper.BinFolder;
            configuration.FirstBinPath.Assemblies.Add( AspNetServerTestHelperExtensions.HttpEndpointAssembly );
            configuration.FirstBinPath.Types.Add( typeof( ClientDirectory ),
                                                  typeof( ClientsHosting ),
                                                  typeof( PartyComponentDirectory ),
                                                  typeof( PartyClientFeatureDriver ),
                                                  typeof( HostingClientFeature ),
                                                  typeof( HostingClientFeatureDriver ),
                                                  typeof( TestBuildClientFeature ),
                                                  typeof( TestBuildClientFeatureDriver ),
                                                  typeof( DesktopWindowClientFeature ),
                                                  typeof( DesktopWindowClientFeatureDriver ) );
            configuration.EnsureAspect<TypeScriptAspectConfiguration>();
            var app = configuration.FirstBinPath.EnsureAspect<TypeScriptBinPathAspectConfiguration>();
            app.Name = "App";
            app.Kind = ClientKind.DefaultApp;
            app.DevServerPort = 4200;
            app.TargetProjectPath = root.AppendPart( "App" );
            app.IntegrationMode = CKGenIntegrationMode.None;
            app.AddOtherConfiguration( new TypeScriptBinPathAspectConfiguration
            {
                Name = "MiniApp",
                Kind = ClientKind.App,
                DevServerPort = 4201,
                TargetProjectPath = root.AppendPart( "MiniApp" ),
                IntegrationMode = CKGenIntegrationMode.None
            } );
            _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
            CreateBuiltClient( root.AppendPart( "App" ), "App" );
            CreateBuiltClient( root.AppendPart( "MiniApp" ), "MiniApp" );
        }
        return _map;

        static void CreateBuiltClient( NormalizedPath projectPath, string name )
        {
            var output = projectPath.Combine( $"dist/{name}/browser" );
            Directory.CreateDirectory( output );
            File.WriteAllText( output.AppendPart( "index.html" ), name );
        }
    }

    // Both clients are declared (web build and hosting), the "DesktopWindow" sections are the given ones.
    static async Task<RunningAspNetServer> CreateHostAsync( params (string Key, string Value)[] desktopWindow )
    {
        return await (await GetMapAsync()).CreateRunningAspNetServerAsync( identity: c =>
        {
            foreach( var client in new[] { "App", "MiniApp" } )
            {
                c[$"Local:PartyClient:{client}:TestBuild"] = "true";
                c[$"Local:PartyClient:{client}:Hosting"] = "true";
            }
            foreach( var (k, v) in desktopWindow ) c[$"Local:PartyClient:{k}"] = v;
        }, configureHost: b => b.Environment.EnvironmentName = "Production" );
    }

    static DesktopWindowClientFeature? FindFeature( RunningAspNetServer server )
    {
        return server.Services.GetRequiredService<IApplicationIdentityService>()
                     .GetRequiredFeature<PartyClientFeature>()
                     .Clients.Select( c => c.GetFeature<DesktopWindowClientFeature>() )
                     .SingleOrDefault( f => f != null );
    }

    [Test]
    public async Task without_the_feature_no_client_has_a_window_Async()
    {
        await using var server = await CreateHostAsync();
        FindFeature( server ).ShouldBeNull();
    }

    [Test]
    public async Task the_window_shows_its_client_with_the_defaults_Async()
    {
        await using var server = await CreateHostAsync( ("MiniApp:DesktopWindow", "true") );
        var f = FindFeature( server );
        f.ShouldNotBeNull();
        f.Client.Name.ShouldBe( "MiniApp" );
        f.Settings.ShouldBe( new DesktopWindowSettings( "MiniApp", 1280, 800 ) );
        f.Url.ShouldNotBeNull().ShouldEndWith( "/mini-app/" );
        (await (await server.Client.GetAsync( new Uri( f.Url ).AbsolutePath )).Content.ReadAsStringAsync()).ShouldBe( "MiniApp" );
    }

    [Test]
    public async Task the_settings_are_read_Async()
    {
        await using var server = await CreateHostAsync( ("App:DesktopWindow:Title", "The title"),
                                                        ("App:DesktopWindow:Width", "800"),
                                                        ("App:DesktopWindow:Height", "600") );
        var f = FindFeature( server ).ShouldNotBeNull();
        f.Settings.ShouldBe( new DesktopWindowSettings( "The title", 800, 600 ) );
        f.Url.ShouldNotBeNull().ShouldEndWith( "/" );
        new Uri( f.Url ).AbsolutePath.ShouldBe( "/" );
    }

    [TestCase( "Width", "0" )]
    [TestCase( "Height", "big" )]
    [TestCase( "Color", "Blue" )]
    public async Task an_invalid_setting_is_a_setup_error_Async( string key, string value )
    {
        await Should.ThrowAsync<Exception>( () => CreateHostAsync( ($"App:DesktopWindow:{key}", value) ) );
    }

    [Test]
    public async Task a_desktop_application_has_one_window_Async()
    {
        await Should.ThrowAsync<Exception>( () => CreateHostAsync( ("App:DesktopWindow", "true"), ("MiniApp:DesktopWindow", "true") ) );
    }
}
