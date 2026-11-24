using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppHost.Desktop.Tests;

/// <summary>
/// The client application of a published host is served from its files (CK.TS.AspNet.Clients: default files, static
/// files and the fallback to index.html).
/// </summary>
[TestFixture]
public class DesktopInProcessBridgeStaticFilesTests
{
    WebApplication _app = null!;
    TestServer _server = null!;
    string _folder = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "StaticClient" );
        Directory.CreateDirectory( _folder );
        File.WriteAllText( Path.Combine( _folder, "index.html" ), "<html><body>The client</body></html>" );
        File.WriteAllText( Path.Combine( _folder, "main.js" ), new string( 'x', 1_000_000 ) );
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer( o => o.BaseAddress = new Uri( "app://localhost" ) );
        _app = builder.Build();
        var files = new PhysicalFileProvider( _folder );
        var options = new StaticFileOptions { FileProvider = files, RequestPath = "" };
        _app.UseDefaultFiles( new DefaultFilesOptions { FileProvider = files, RequestPath = "" } );
        _app.UseStaticFiles( options );
        _app.MapFallbackToFile( "{*path:nonfile}", "index.html", options );
        await _app.StartAsync();
        _server = _app.GetTestServer();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestCase( "/" )]
    [TestCase( "/index.html" )]
    [TestCase( "/deep/route" )]
    [TestCase( "/main.js" )]
    [CancelAfter( 10000 )]
    public async Task the_files_are_served_Async( string path, CancellationToken cancel )
    {
        var bridge = new DesktopInProcessBridge();
        bridge.SetServer( _server, "app://localhost/" );
        var context = await bridge.GetAsync( _server, path, cancel ).WaitAsync( TimeSpan.FromSeconds( 5 ), cancel );
        context.Response.StatusCode.ShouldBe( 200 );
        var (body, contentType) = await Task.Run( () => bridge.HandleScheme( null!, "app://localhost" + path ), cancel ).WaitAsync( TimeSpan.FromSeconds( 5 ), cancel );
        contentType.ShouldStartWith( path == "/main.js" ? "text/javascript" : "text/html" );
        body.Length.ShouldBeGreaterThan( 10 );
    }
}
