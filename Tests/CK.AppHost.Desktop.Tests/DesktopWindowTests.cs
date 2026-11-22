using CK.AppIdentity;
using CK.Core;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;
using System;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Desktop.Tests;

[TestFixture]
public class DesktopWindowTests
{
    static IStObjMap? _map;

    static async Task<IStObjMap> GetMapAsync()
    {
        if( _map == null )
        {
            var configuration = TestHelper.CreateDefaultEngineConfiguration();
            configuration.FirstBinPath.Path = TestHelper.BinFolder;
            configuration.FirstBinPath.Assemblies.Add( AspNetServerTestHelperExtensions.HttpEndpointAssembly );
            configuration.FirstBinPath.Types.Add( typeof( DesktopWindowFeatureDriver ) );
            _map = (await configuration.RunSuccessfullyAsync()).LoadMap();
        }
        return _map;
    }

    [Test]
    public async Task without_the_feature_there_is_no_window_Async()
    {
        await using var server = await (await GetMapAsync()).CreateRunningAspNetServerAsync();
        var driver = server.Services.GetRequiredService<DesktopWindowFeatureDriver>();
        driver.IsOpen.ShouldBeFalse();
        driver.Closed.IsCompleted.ShouldBeTrue();
    }

    [TestCase( """{ "Width": 0 }""" )]
    [TestCase( """{ "Height": "big" }""" )]
    public async Task an_invalid_size_is_a_setup_error_Async( string json )
    {
        var map = await GetMapAsync();
        await Should.ThrowAsync<Exception>( async () =>
        {
            await using var server = await map.CreateRunningAspNetServerAsync( identity: c =>
            {
                var o = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
                foreach( var (k, v) in o ) c[$"Local:DesktopWindow:{k}"] = v!.ToString();
            } );
        } );
    }

    /// <summary>
    /// Opens a real window (a desktop session is required): the window shows the endpoint, closing it stops the host, and
    /// stopping the host closes it.
    /// </summary>
    [Test]
    [Explicit( "Opens a window on the desktop." )]
    public async Task the_window_and_the_host_stop_together_Async()
    {
        var map = await GetMapAsync();
        // Closing the window stops the host.
        await using( var server = await map.CreateRunningAspNetServerAsync( identity: c => c["Local:DesktopWindow:Title"] = "CK.AppHost.Desktop test" ) )
        {
            var driver = server.Services.GetRequiredService<DesktopWindowFeatureDriver>();
            driver.IsOpen.ShouldBeTrue();
            var lifetime = server.Services.GetRequiredService<IHostApplicationLifetime>();
            await Task.Delay( 2000 );
            driver.CloseWindow();
            await driver.Closed.WaitAsync( TimeSpan.FromSeconds( 10 ) );
            lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeTrue();
        }
        // Stopping the host closes the window.
        DesktopWindowFeatureDriver second;
        await using( var server = await map.CreateRunningAspNetServerAsync( identity: c => c["Local:DesktopWindow"] = "true" ) )
        {
            second = server.Services.GetRequiredService<DesktopWindowFeatureDriver>();
            second.IsOpen.ShouldBeTrue();
            await Task.Delay( 2000 );
        }
        second.Closed.IsCompleted.ShouldBeTrue();
        second.IsOpen.ShouldBeFalse();
    }
}
