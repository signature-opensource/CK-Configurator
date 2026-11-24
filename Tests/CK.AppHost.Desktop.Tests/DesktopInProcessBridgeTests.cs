using CK.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CK.AppHost.Desktop.Tests;

[TestFixture]
public class DesktopInProcessBridgeTests
{
    WebApplication _app = null!;
    TestServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer( o => o.BaseAddress = new Uri( "app://localhost" ) );
        _app = builder.Build();
        _app.MapPost( "/echo", async ( HttpContext c ) =>
        {
            using var r = new StreamReader( c.Request.Body );
            var body = await r.ReadToEndAsync();
            return Results.Json( new
            {
                body,
                origin = c.Request.Headers.Origin.ToString(),
                site = c.Request.Headers["Sec-Fetch-Site"].ToString(),
                custom = c.Request.Headers["X-Custom"].ToString(),
                query = c.Request.QueryString.Value
            } );
        } );
        _app.MapGet( "/login", ( HttpContext c ) =>
        {
            c.Response.Cookies.Append( "session", "42" );
            return "logged";
        } );
        _app.MapGet( "/whoami", ( HttpContext c ) => c.Request.Cookies["session"] ?? "nobody" );
        _app.MapGet( "/binary", () => Results.Bytes( [0, 1, 2, 255], "application/octet-stream" ) );
        _app.MapGet( "/accept", ( HttpContext c ) => Results.Text( c.Request.Headers.Accept.ToString(), "text/plain" ) );
        _app.MapGet( "/accept.js", ( HttpContext c ) => Results.Text( c.Request.Headers.Accept.ToString(), "text/plain" ) );
        await _app.StartAsync();
        _server = _app.GetTestServer();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _app.DisposeAsync();

    static JsonElement Parse( string response ) => JsonDocument.Parse( response ).RootElement;

    DesktopInProcessBridge CreateBridge()
    {
        var bridge = new DesktopInProcessBridge();
        bridge.SetServer( _server, "app://localhost/" );
        return bridge;
    }

    [Test]
    public async Task a_request_is_relayed_with_its_body_headers_and_query_and_the_same_origin_headers_Async()
    {
        var bridge = CreateBridge();
        var request = JsonSerializer.Serialize( new
        {
            id = 7,
            method = "POST",
            url = "/echo?x=1",
            headers = new { X_Custom = "a", Content_Type = "application/json" },
            body = "{\"some\":\"json\"}"
        } ).Replace( "X_Custom", "X-Custom" ).Replace( "Content_Type", "Content-Type" );
        var r = Parse( await bridge.ProcessAsync( request ) );
        r.GetProperty( "id" ).GetInt32().ShouldBe( 7 );
        r.GetProperty( "status" ).GetInt32().ShouldBe( 200 );
        r.GetProperty( "base64" ).GetBoolean().ShouldBeFalse();
        r.GetProperty( "headers" ).GetProperty( "Content-Type" ).GetString().ShouldStartWith( "application/json" );
        var echo = Parse( r.GetProperty( "body" ).GetString()! );
        echo.GetProperty( "body" ).GetString().ShouldBe( "{\"some\":\"json\"}" );
        echo.GetProperty( "origin" ).GetString().ShouldBe( "app://localhost" );
        echo.GetProperty( "site" ).GetString().ShouldBe( "same-origin" );
        echo.GetProperty( "custom" ).GetString().ShouldBe( "a" );
        echo.GetProperty( "query" ).GetString().ShouldBe( "?x=1" );
    }

    [Test]
    public async Task the_cookies_set_by_the_server_are_sent_back_Async()
    {
        var bridge = CreateBridge();
        Parse( await bridge.ProcessAsync( """{ "id": 1, "method": "GET", "url": "/whoami" }""" ) ).GetProperty( "body" ).GetString().ShouldBe( "nobody" );
        var login = Parse( await bridge.ProcessAsync( """{ "id": 2, "method": "GET", "url": "/login" }""" ) );
        // The cookies are kept by the bridge: they are not given to the page.
        login.GetProperty( "headers" ).TryGetProperty( "Set-Cookie", out _ ).ShouldBeFalse();
        Parse( await bridge.ProcessAsync( """{ "id": 3, "method": "GET", "url": "/whoami" }""" ) ).GetProperty( "body" ).GetString().ShouldBe( "42" );
    }

    [Test]
    public async Task a_binary_response_is_base64_and_an_unknown_route_is_a_404_Async()
    {
        var bridge = CreateBridge();
        var r = Parse( await bridge.ProcessAsync( """{ "id": 1, "method": "GET", "url": "/binary" }""" ) );
        r.GetProperty( "base64" ).GetBoolean().ShouldBeTrue();
        Convert.FromBase64String( r.GetProperty( "body" ).GetString()! ).ShouldBe( new byte[] { 0, 1, 2, 255 } );
        Parse( await bridge.ProcessAsync( """{ "id": 2, "method": "GET", "url": "/nowhere" }""" ) ).GetProperty( "status" ).GetInt32().ShouldBe( 404 );
    }

    [Test]
    public async Task without_server_the_request_is_a_503_Async()
    {
        var bridge = new DesktopInProcessBridge();
        var r = Parse( await bridge.ProcessAsync( """{ "id": 5, "method": "GET", "url": "/" }""" ) );
        r.GetProperty( "id" ).GetInt32().ShouldBe( 5 );
        r.GetProperty( "status" ).GetInt32().ShouldBe( 503 );
    }

    [Test]
    public void the_scheme_serves_the_page_and_never_a_download()
    {
        var bridge = CreateBridge();
        // A document is asked as a navigation (a dev server serves its index to it), a file as "*/*".
        var (body, contentType) = bridge.HandleScheme( null!, "app://localhost/accept" );
        contentType.ShouldStartWith( "text/plain" );
        new StreamReader( body ).ReadToEnd().ShouldStartWith( "text/html" );
        (body, _) = bridge.HandleScheme( null!, "app://localhost/accept.js" );
        new StreamReader( body ).ReadToEnd().ShouldBe( "*/*" );
        // An error without content is a text (no content type would be a download for the window).
        (body, contentType) = bridge.HandleScheme( null!, "app://localhost/nowhere" );
        contentType.ShouldBe( "text/plain" );
        new StreamReader( body ).ReadToEnd().ShouldBe( "404 Not Found: /nowhere" );
        // Before the endpoint is started: a waiting page.
        (_, contentType) = new DesktopInProcessBridge().HandleScheme( null!, "app://localhost/" );
        contentType.ShouldBe( "text/html" );
    }
}
