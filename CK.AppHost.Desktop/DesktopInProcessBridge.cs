using CK.AppIdentity;
using InfiniFrame;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.TestHost;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Core;

/// <summary>
/// The bridge between the desktop window and an in-process endpoint (the "InProcess" option of the HttpEndpoint,
/// CK.AppIdentity.HttpEndpoint.InProcess): no port is opened.
/// <list type="bullet">
///   <item>
///   The "app" scheme of the window (the only custom scheme that InfiniFrame registers as a secure origin with an
///   authority: "app://localhost") serves the GET requests of the page (its files): they are sent to the in-process server.
///   </item>
///   <item>
///   The other requests of the page (its API: Cris) are messages: the axios adapter of CK.Ng.AppHost.Desktop posts
///   <c>{ id, method, url, headers, body }</c> with the envelope id <see cref="MessageId"/>, the bridge answers
///   <c>{ id, status, headers, body, base64 }</c> with the same envelope id.
///   </item>
/// </list>
/// The cookies that the server sets are kept by the bridge (the browser's cookie jar doesn't see these requests).
/// </summary>
sealed class DesktopInProcessBridge
{
    /// <summary>
    /// The custom scheme of the window: "app" (see the remarks of the class).
    /// </summary>
    public const string Scheme = "app";

    /// <summary>
    /// The envelope id of the requests and of the responses.
    /// </summary>
    public const string MessageId = "ck:http";

    static readonly Uri _jarUri = new( "http://localhost/" );
    readonly CookieContainer _cookies = new();
    volatile TestServer? _server;
    readonly TaskCompletionSource _pageRequested = new( TaskCreationOptions.RunContinuationsAsynchronously );
    string _origin = "app://localhost";

    /// <summary>
    /// Sets the in-process server once the endpoint is started.
    /// </summary>
    /// <param name="server">The in-process server.</param>
    /// <param name="serverUrl">The URL of the server (the origin of the page).</param>
    public void SetServer( TestServer server, string serverUrl )
    {
        _origin = serverUrl.TrimEnd( '/' );
        _server = server;
    }

    /// <summary>
    /// Waits for the first request of the page (a navigation that reached the scheme).
    /// </summary>
    /// <param name="timeout">The maximal time to wait.</param>
    /// <returns>True when the page has asked a file, false on timeout.</returns>
    public async Task<bool> WaitForPageAsync( TimeSpan timeout )
    {
        try
        {
            await _pageRequested.Task.WaitAsync( timeout ).ConfigureAwait( false );
            return true;
        }
        catch( TimeoutException )
        {
            return false;
        }
    }

    /// <summary>
    /// The handler of the "app" scheme: a GET of the page (no status code: InfiniFrame's handler returns a body and a
    /// content type only).
    /// </summary>
    public (Stream, string) HandleScheme( IInfiniFrameWindow window, string url )
    {
        var server = _server;
        if( server == null )
        {
            return (new MemoryStream( "<html><body>starting...</body></html>"u8.ToArray() ), "text/html");
        }
        try
        {
            _pageRequested.TrySetResult();
            var uri = new Uri( url );
            // The handler is synchronous: the request runs on the thread pool.
            var context = Task.Run( () => GetAsync( server, uri.PathAndQuery, default ) ).GetAwaiter().GetResult();
            var body = new MemoryStream();
            context.Response.Body.CopyTo( body );
            body.Position = 0;
            var contentType = context.Response.ContentType;
            if( context.Response.StatusCode >= 400 && (contentType == null || body.Length == 0) )
            {
                // No content type would be a download for the window: an error is a text.
                var message = $"{context.Response.StatusCode} {ReasonPhrases.GetReasonPhrase( context.Response.StatusCode )}: {uri.PathAndQuery}";
                return (new MemoryStream( Encoding.UTF8.GetBytes( message ) ), "text/plain");
            }
            return (body, contentType ?? GuessContentType( uri.AbsolutePath ));
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"While serving '{url}' in the desktop window.", ex );
            return (new MemoryStream( Encoding.UTF8.GetBytes( WebUtility.HtmlEncode( ex.Message ) ) ), "text/plain");
        }
    }

    /// <summary>
    /// Sends a GET of the page to the in-process server. InfiniFrame gives the url only: the Accept header is the one of a
    /// navigation for a document (no extension or ".html": a dev server serves its index to it), "*/*" otherwise.
    /// </summary>
    public Task<HttpContext> GetAsync( TestServer server, string pathAndQuery, CancellationToken cancel )
    {
        int q = pathAndQuery.IndexOf( '?' );
        var path = q < 0 ? pathAndQuery : pathAndQuery.Substring( 0, q );
        var ext = Path.GetExtension( path );
        var accept = ext.Length == 0 || ext.Equals( ".html", StringComparison.OrdinalIgnoreCase )
                        ? "text/html,application/xhtml+xml,*/*;q=0.8"
                        : "*/*";
        return SendAsync( server, "GET", pathAndQuery, new Dictionary<string, string> { ["Accept"] = accept }, null, cancel );
    }

    static string GuessContentType( string path )
    {
        return _contentTypes.TryGetContentType( path, out var contentType )
                ? contentType
                : Path.GetExtension( path ).Length == 0 ? "text/html" : "application/octet-stream";
    }

    static readonly FileExtensionContentTypeProvider _contentTypes = new();

    /// <summary>
    /// The handler of the <see cref="MessageId"/> messages: the request runs on the thread pool, the response is sent back.
    /// </summary>
    public void HandleMessage( IInfiniFrameWindow window, string? payload )
    {
        if( string.IsNullOrEmpty( payload ) ) return;
        _ = Task.Run( () => RelayAsync( window, payload ) );
    }

    async Task RelayAsync( IInfiniFrameWindow window, string payload )
    {
        var response = await ProcessAsync( payload ).ConfigureAwait( false );
        var envelope = JsonSerializer.Serialize( new { id = MessageId, command = "Post", version = 2, data = response } );
        await window.SendWebMessageAsync( envelope, default ).ConfigureAwait( false );
    }

    /// <summary>
    /// Runs a request <c>{ id, method, url, headers, body }</c> and returns its response
    /// <c>{ id, status, headers, body, base64 }</c> (the data of the response message).
    /// </summary>
    /// <param name="payload">The request.</param>
    /// <returns>The response.</returns>
    internal async Task<string> ProcessAsync( string payload )
    {
        int id = 0;
        string response;
        try
        {
            var request = JsonDocument.Parse( payload ).RootElement;
            id = request.GetProperty( "id" ).GetInt32();
            var server = _server;
            if( server == null )
            {
                response = Error( id, 503, "The in-process endpoint is not started." );
            }
            else
            {
                Dictionary<string, string>? headers = null;
                if( request.TryGetProperty( "headers", out var h ) && h.ValueKind == JsonValueKind.Object )
                {
                    headers = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
                    foreach( var p in h.EnumerateObject() )
                    {
                        if( p.Value.ValueKind == JsonValueKind.String ) headers[p.Name] = p.Value.GetString()!;
                    }
                }
                string? body = request.TryGetProperty( "body", out var b ) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                var context = await SendAsync( server,
                                               request.GetProperty( "method" ).GetString() ?? "GET",
                                               request.GetProperty( "url" ).GetString() ?? "/",
                                               headers,
                                               body,
                                               default ).ConfigureAwait( false );
                response = await WriteResponseAsync( id, context ).ConfigureAwait( false );
            }
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( "While relaying a request of the desktop window.", ex );
            response = Error( id, 500, ex.Message );
        }
        return response;
    }

    async Task<HttpContext> SendAsync( TestServer server,
                                       string method,
                                       string pathAndQuery,
                                       Dictionary<string, string>? headers,
                                       string? body,
                                       CancellationToken cancel )
    {
        var context = await server.SendAsync( c =>
        {
            var r = c.Request;
            r.Method = method;
            int q = pathAndQuery.IndexOf( '?' );
            r.Path = PathString.FromUriComponent( q < 0 ? pathAndQuery : pathAndQuery.Substring( 0, q ) );
            if( q >= 0 ) r.QueryString = new QueryString( pathAndQuery.Substring( q ) );
            if( headers != null )
            {
                foreach( var (k, v) in headers ) r.Headers[k] = v;
            }
            // Only the page of the window sends these requests: it is the same origin.
            r.Headers.Origin = _origin;
            r.Headers["Sec-Fetch-Site"] = "same-origin";
            var cookies = _cookies.GetCookieHeader( _jarUri );
            if( cookies.Length > 0 ) r.Headers.Cookie = cookies;
            if( body != null )
            {
                var bytes = Encoding.UTF8.GetBytes( body );
                r.Body = new MemoryStream( bytes );
                r.ContentLength = bytes.Length;
            }
        }, cancel ).ConfigureAwait( false );
        foreach( var setCookie in context.Response.Headers.SetCookie )
        {
            if( setCookie != null ) _cookies.SetCookies( _jarUri, setCookie );
        }
        return context;
    }

    static async Task<string> WriteResponseAsync( int id, HttpContext context )
    {
        var r = context.Response;
        var body = new MemoryStream();
        await r.Body.CopyToAsync( body ).ConfigureAwait( false );
        var contentType = r.ContentType ?? "";
        bool isText = contentType.StartsWith( "text/", StringComparison.OrdinalIgnoreCase )
                      || contentType.Contains( "json", StringComparison.OrdinalIgnoreCase )
                      || contentType.Contains( "javascript", StringComparison.OrdinalIgnoreCase )
                      || contentType.Contains( "xml", StringComparison.OrdinalIgnoreCase );
        var headers = new Dictionary<string, string>();
        foreach( var (k, v) in r.Headers )
        {
            if( !k.Equals( "Set-Cookie", StringComparison.OrdinalIgnoreCase ) ) headers[k] = v.ToString();
        }
        var bytes = body.ToArray();
        return JsonSerializer.Serialize( new
        {
            id,
            status = r.StatusCode,
            headers,
            body = isText ? Encoding.UTF8.GetString( bytes ) : Convert.ToBase64String( bytes ),
            base64 = !isText
        } );
    }

    static string Error( int id, int status, string message )
    {
        return JsonSerializer.Serialize( new { id, status, headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" }, body = message, base64 = false } );
    }
}
