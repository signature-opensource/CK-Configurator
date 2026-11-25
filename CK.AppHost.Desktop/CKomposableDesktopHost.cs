using CK.AppIdentity;
using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Core;

/// <summary>
/// The bootstrap of a CKomposable desktop application: the <c>Program.cs</c> of its host is
/// <code>
/// return CK.Core.CKomposableDesktopHost.Run( args );
/// </code>
/// (synchronous: the window must run on the main thread of the process on macOS).
/// <para>
/// When a client of the root party has the "DesktopWindow" feature (<see cref="DesktopWindowClientFeature"/>), the window
/// opens at once on a waiting page, the host starts in the background, then the window loads the client once it answers.
/// A failure of the host (an invalid configuration, a setup error...) is shown in the window. Closing the window stops the
/// host; stopping the host closes the window. Without the feature, the host runs as with
/// <see cref="CKomposableAppHost.RunAsync(string[], Action{IActivityMonitor, Microsoft.Extensions.Hosting.HostApplicationBuilder, IStObjMap}?)"/>.
/// </para>
/// <para>
/// The window runs on the main thread, except on Windows where it runs on an STA thread of its own (WebView2 requires an
/// STA thread, and the main thread of a top-level <c>Program.cs</c> is not one).
/// </para>
/// </summary>
public static class CKomposableDesktopHost
{
    /// <summary>
    /// Builds the host of the entry assembly and runs it, with its desktop window when a client of the root party has the
    /// "DesktopWindow" feature.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The exit code of the process: 0 on success, 1 on failure.</returns>
    public static int Run( string[] args )
    {
        SetPublishedLogPath();
        IHost host;
        try
        {
            host = CKomposableAppHost.Build( args );
        }
        catch( Exception ex )
        {
            ReportFailure( ex );
            // A desktop application has no console to show it: a window shows it (if a window can be opened).
            TryShowFailure( "The application failed to start", ex );
            return 1;
        }
        using( host )
        {
            var settings = FindWindowSettings( host.Services.GetRequiredService<IConfiguration>() );
            if( settings == null )
            {
                try
                {
                    host.Run();
                    return 0;
                }
                catch( Exception ex )
                {
                    ReportFailure( ex );
                    return 1;
                }
            }
            return RunWithWindow( host, settings );
        }
    }

    // The "DesktopWindow" of a client of the root party, read before the host starts: the window opens at once. The
    // feature checks it again when the host starts (an error is then shown in the window).
    static DesktopWindowSettings? FindWindowSettings( IConfiguration configuration )
    {
        var clients = configuration.GetSection( "CK-AppIdentity:Local:PartyClient" );
        foreach( var c in clients.GetChildren() )
        {
            var w = c.GetSection( "DesktopWindow" );
            if( !w.Exists() || string.Equals( w.Value, "false", StringComparison.OrdinalIgnoreCase ) ) continue;
            // Invalid settings are a setup error (shown in the window): the window opens with the defaults.
            return DesktopWindowSettings.Peek( w, c.Key );
        }
        return null;
    }

    /// <summary>
    /// The logs of a published desktop application are in the user's application data: its folder may be read only (an
    /// installed application) or temporary (the extraction folder of a single file application).
    /// <c>{LocalApplicationData}/{Domain}-{Party}/Logs</c>, from the full name of its deployment ("CK/$Configurator/#Production"
    /// gives "CK-Configurator"), or the name of the host without ".Host".
    /// </summary>
    static void SetPublishedLogPath()
    {
        if( LogFile.RootLogPath != null ) return;
        var published = Path.Combine( AppContext.BaseDirectory, CKomposableAppHost.PublishedFileName );
        if( !IsPublished ) return;
        string? name = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse( File.ReadAllText( published ) );
            if( doc.RootElement.TryGetProperty( "Deployment", out var d )
                && d.ValueKind == System.Text.Json.JsonValueKind.Object
                && d.TryGetProperty( "FullName", out var f )
                && f.GetString() is { Length: > 0 } fullName )
            {
                name = string.Join( '-', fullName.Split( '/' ).Where( s => !s.StartsWith( '#' ) ).Select( s => s.TrimStart( '$' ) ) );
            }
        }
        catch( Exception ex )
        {
            ReportFailure( ex );
        }
        if( string.IsNullOrEmpty( name ) )
        {
            name = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "CKomposable";
            if( name.EndsWith( ".Host", StringComparison.OrdinalIgnoreCase ) ) name = name[..^5];
        }
        LogFile.RootLogPath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), name, "Logs" );
    }

    /// <summary>
    /// Gets whether this host is published (its <see cref="CKomposableAppHost.PublishedFileName"/> is next to it), not
    /// run from its project in Development.
    /// </summary>
    internal static bool IsPublished => File.Exists( Path.Combine( AppContext.BaseDirectory, CKomposableAppHost.PublishedFileName ) );

    static volatile IInfiniFrameWindow? _window;

    /// <summary>
    /// Gets the window of the application (null without window): the commands of the desktop use it (a dialog).
    /// </summary>
    internal static IInfiniFrameWindow? Window => _window;

    static int RunWithWindow( IHost host, DesktopWindowSettings settings )
    {
        int exitCode = 0;
        Task? hostTask = null;
        // The bridge to an in-process endpoint: its handlers are registered with the window (they answer "starting..."
        // until the endpoint is started, and are unused when the endpoint runs on Kestrel).
        var bridge = new DesktopInProcessBridge();
        // The client is loaded once the start page is loaded (it posts a message): a navigation requested before (before the
        // window is created, or while its start page loads) is lost or replaced by the start page.
        var startPageLoaded = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        void RunWindow()
        {
            var window = InfiniFrameWindowBuilder.Create()
                            .SetTitle( settings.Title )
                            .SetSize( settings.Width, settings.Height )
                            // The browser's menu (Back, Reload, Save as...) is not the one of an application: the client
                            // shows its own menus. It is kept in Development (Inspect).
                            .EnableContextMenu( !IsPublished )
                            .SetStartPageContent( Page( settings.Title, "starting...", null, readyMessageId: _startPageLoadedId ) )
                            .RegisterWebMessagePostHandler( _startPageLoadedId, ( IInfiniFrameWindow w, string? payload ) => startPageLoaded.TrySetResult() )
                            .RegisterCustomSchemeHandler( DesktopInProcessBridge.Scheme, bridge.HandleScheme )
                            .RegisterWebMessagePostHandler( DesktopInProcessBridge.MessageId, bridge.HandleMessage )
                            .Build();
            _window = window;
            hostTask = Task.Run( async () =>
            {
                if( !await RunHostAsync( host, window, settings, bridge, startPageLoaded.Task ).ConfigureAwait( false ) ) Interlocked.Exchange( ref exitCode, 1 );
            } );
            window.WaitForClose();
        }
        try
        {
            if( OperatingSystem.IsWindows() )
            {
                var t = new Thread( RunWindow ) { Name = "DesktopWindow" };
                t.SetApartmentState( ApartmentState.STA );
                t.Start();
                t.Join();
            }
            else
            {
                RunWindow();
            }
        }
        catch( Exception ex )
        {
            ReportFailure( ex );
            exitCode = 1;
        }
        // The window is closed: the host stops (nothing happens when it is already stopped).
        host.Services.GetService<IHostApplicationLifetime>()?.StopApplication();
        if( hostTask != null && !hostTask.Wait( TimeSpan.FromSeconds( 30 ) ) )
        {
            ActivityMonitor.StaticLogger.Warn( "The host didn't stop in 30 seconds after the desktop window was closed." );
        }
        return exitCode;
    }

    // Starts the host, loads the client in the window, and waits for the host to stop. False on failure (it is shown in
    // the window that stays open until it is closed).
    static async Task<bool> RunHostAsync( IHost host,
                                          IInfiniFrameWindow window,
                                          DesktopWindowSettings settings,
                                          DesktopInProcessBridge bridge,
                                          Task startPageLoaded )
    {
        // The errors logged while the host starts: shown in the window on failure (the exception only says that a setup
        // failed). The monitors created while it starts collect them.
        var errors = new ConcurrentQueue<string>();
        Action<IActivityMonitor> collect = m => m.Output.RegisterClient( new ErrorCollector( errors ) );
        ActivityMonitor.AutoConfiguration += collect;
        try
        {
            try
            {
                await host.StartAsync().ConfigureAwait( false );
            }
            finally
            {
                ActivityMonitor.AutoConfiguration -= collect;
            }
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            // Stopping the host closes the window.
            lifetime.ApplicationStopping.Register( () =>
            {
                if( !window.IsClosedOrClosing() ) window.Close();
            } );
            var feature = host.Services.GetRequiredService<ApplicationIdentityService>()
                              .GetFeature<PartyClientFeature>()?
                              .Clients.Select( c => c.GetFeature<DesktopWindowClientFeature>() )
                              .FirstOrDefault( f => f != null );
            var url = feature?.Url;
            if( url == null )
            {
                var message = feature == null
                                ? "No client of the root party has the \"DesktopWindow\" feature."
                                : $"The {feature.Client} has no url: the endpoint of the party is not started.";
                ActivityMonitor.StaticLogger.Error( message );
                await ShowFailureAsync( window, settings.Title, message, [], null ).ConfigureAwait( false );
                return false;
            }
            ActivityMonitor.StaticLogger.Info( $"Desktop window on '{url}'." );
            await WaitForStartPageAsync( startPageLoaded ).ConfigureAwait( false );
            // An in-process endpoint ("InProcess" option): the window relays the requests of its page, no port is opened.
            var server = host.Services.GetRequiredService<HttpEndpointFeatureDriver>().Endpoint?.GetInProcessServer();
            if( server != null )
            {
                if( !url.StartsWith( DesktopInProcessBridge.Scheme + "://", StringComparison.OrdinalIgnoreCase ) )
                {
                    var message = $"The in-process endpoint's URL is '{url}': the desktop window serves only \"{DesktopInProcessBridge.Scheme}://\" URLs (\"InProcess\": true).";
                    ActivityMonitor.StaticLogger.Error( message );
                    await ShowFailureAsync( window, settings.Title, message, [], null ).ConfigureAwait( false );
                    return false;
                }
                bridge.SetServer( server, url );
                await LoadWhenReadyAsync( window, url, u => bridge.GetAsync( server, new Uri( u ).PathAndQuery, default ).ContinueWith( t => t.Result.Response.StatusCode ) ).ConfigureAwait( false );
                // The page asks its files to the bridge: a navigation that didn't reach it is requested again.
                for( int retry = 0; retry < 10 && !window.IsClosedOrClosing(); ++retry )
                {
                    if( await bridge.WaitForPageAsync( TimeSpan.FromSeconds( 2 ) ).ConfigureAwait( false ) ) break;
                    ActivityMonitor.StaticLogger.Warn( $"The desktop window didn't load '{url}': it is loaded again." );
                    Load( window, url );
                }
            }
            else
            {
                await LoadWhenReadyAsync( window, url ).ConfigureAwait( false );
            }
            await host.WaitForShutdownAsync().ConfigureAwait( false );
            return true;
        }
        catch( Exception ex )
        {
            ReportFailure( ex );
            await ShowFailureAsync( window, settings.Title, "The application failed to start.", errors, ex ).ConfigureAwait( false );
            return false;
        }
    }

    // Loads the url once it answers (anything but a server error: a forwarder answers 502 while the dev server of a client
    // in Development is not listening). After the delay, the url is loaded anyway: the window shows what it answers.
    // The in-process endpoint is asked through its server (getStatus), the Kestrel one with an HttpClient.
    static async Task LoadWhenReadyAsync( IInfiniFrameWindow window, string url, Func<string, Task<int>>? getStatus = null )
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds( 10 ) };
            getStatus ??= async u =>
            {
                using var response = await http.GetAsync( u, HttpCompletionOption.ResponseHeadersRead ).ConfigureAwait( false );
                return (int)response.StatusCode;
            };
            var limit = DateTime.UtcNow + _readyDelay;
            while( !window.IsClosedOrClosing() && DateTime.UtcNow < limit )
            {
                try
                {
                    if( await getStatus( url ).ConfigureAwait( false ) < 500 ) break;
                }
                catch( HttpRequestException ) { }
                catch( TaskCanceledException ) { }
                await Task.Delay( 500 ).ConfigureAwait( false );
            }
            if( window.IsClosedOrClosing() ) return;
            Load( window, url );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"While loading '{url}' in the desktop window.", ex );
        }
    }

    // InfiniFrame only navigates to a trusted origin (a refused navigation is silent): the window opened on a page without
    // origin, the one of the endpoint (its port is known once it is started) is trusted now.
    static void Load( IInfiniFrameWindow window, string url )
    {
        try
        {
            var uri = new Uri( url );
            var policy = InfiniFrameUriSecurityPolicyRegistry.GetForWindow( window );
            var builder = new InfiniFrameUriSecurityPolicyBuilder( policy ).AddTrustedOrigin( uri );
            if( uri.Scheme == DesktopInProcessBridge.Scheme ) builder.AllowNavigationScheme( DesktopInProcessBridge.Scheme );
            InfiniFrameUriSecurityPolicyRegistry.BindToWindow( window, builder.Build() );
            window.Load( uri );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"While loading '{url}' in the desktop window.", ex );
        }
    }

    static readonly TimeSpan _readyDelay = TimeSpan.FromMinutes( 3 );

    const string _startPageLoadedId = "ck:start-page-loaded";

    static async Task WaitForStartPageAsync( Task startPageLoaded )
    {
        try
        {
            await startPageLoaded.WaitAsync( TimeSpan.FromSeconds( 30 ) ).ConfigureAwait( false );
        }
        catch( TimeoutException )
        {
            ActivityMonitor.StaticLogger.Warn( "The start page of the desktop window is not loaded after 30 seconds: the client is loaded anyway." );
        }
    }

    static async Task ShowFailureAsync( IInfiniFrameWindow window, string title, string message, IEnumerable<string> errors, Exception? ex )
    {
        try
        {
            // The host may fail before the window is ready: a page loaded before is lost.
            await window.WaitForReadyAsync().ConfigureAwait( false );
            if( window.IsClosedOrClosing() ) return;
            var result = await window.LoadRawStringAsync( Page( title, message, ex, errors ) ).ConfigureAwait( false );
            ActivityMonitor.StaticLogger.Trace( $"The failure is shown in the desktop window: {result}." );
        }
        catch( Exception e )
        {
            ActivityMonitor.StaticLogger.Error( "While showing the failure in the desktop window.", e );
        }
    }

    // A window only for the failure (the host was not built): nothing more when no window can be opened.
    static void TryShowFailure( string message, Exception ex )
    {
        void Show()
        {
            InfiniFrameWindowBuilder.Create()
                .SetTitle( "Error" )
                .SetSize( 900, 600 )
                .SetStartPageContent( Page( "Error", message, ex ) )
                .Build()
                .WaitForClose();
        }
        try
        {
            if( OperatingSystem.IsWindows() )
            {
                var t = new Thread( Show ) { Name = "DesktopWindow" };
                t.SetApartmentState( ApartmentState.STA );
                t.Start();
                t.Join();
            }
            else Show();
        }
        catch
        {
            // No window: the standard error and the logs say it.
        }
    }

    // The waiting page and the failure page.
    static string Page( string title, string message, Exception? ex, IEnumerable<string>? errors = null, string? readyMessageId = null )
    {
        // The page tells the host that it is loaded: an InfiniFrame envelope posted to the WebView (WebView2 or WebKit).
        var ready = readyMessageId == null
                    ? ""
                    : $$$"""
                        <script>(function(){var e=JSON.stringify({id:'{{{readyMessageId}}}',command:'Post',version:2,data:''});
                        try{if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(e);}
                        else if(window.webkit&&window.webkit.messageHandlers&&window.webkit.messageHandlers.infiniFrameInterop){window.webkit.messageHandlers.infiniFrameInterop.postMessage(e);}}catch(x){}})();</script>
                        """;
        var list = errors == null ? "" : string.Concat( errors.Distinct().Select( e => $"<li>{WebUtility.HtmlEncode( e )}</li>" ) );
        if( list.Length > 0 ) list = $"<ul class=\"errors\">{list}</ul>";
        var t = WebUtility.HtmlEncode( title );
        var m = WebUtility.HtmlEncode( message );
        var details = ex == null ? "" : $"<pre>{WebUtility.HtmlEncode( ex.ToString() )}</pre>";
        var logs = ex == null || LogFile.RootLogPath == null
                    ? ""
                    : $"<p>The logs are in <code>{WebUtility.HtmlEncode( LogFile.RootLogPath )}</code>.</p>";
        var style = ex == null
                    ? "display:flex;height:100vh;margin:0;align-items:center;justify-content:center;color:#555"
                    : "margin:24px;color:#333";
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{{t}}</title>
            <style>body{font-family:Segoe UI,sans-serif;{{style}}} pre{white-space:pre-wrap;font-size:12px;background:#f5f5f5;padding:12px} .errors{color:#b00020}</style>
            </head><body><div><div>{{t}}: {{m}}</div>{{list}}{{logs}}{{details}}</div>{{ready}}</body></html>
            """;
    }

    // The standard error (a console may be attached in Development) and the monitoring when it is still active.
    static void ReportFailure( Exception ex )
    {
        try
        {
            Console.Error.WriteLine( $"The host failed: {ex.Message}" );
            Console.Error.WriteLine( ex );
            if( CK.Monitoring.GrandOutput.Default != null ) ActivityMonitor.StaticLogger.Fatal( "The host failed.", ex );
        }
        catch
        {
            // Nothing more can be done: the exit code says it.
        }
    }

    // Collects the errors (and fatals) of a monitor, groups included.
    sealed class ErrorCollector : ActivityMonitorClient
    {
        readonly ConcurrentQueue<string> _errors;

        public ErrorCollector( ConcurrentQueue<string> errors ) => _errors = errors;

        protected override void OnUnfilteredLog( ref ActivityMonitorLogData data )
        {
            if( data.MaskedLevel >= LogLevel.Error ) _errors.Enqueue( data.Text );
        }

        protected override void OnOpenGroup( IActivityLogGroup group )
        {
            if( group.Data.MaskedLevel >= LogLevel.Error ) _errors.Enqueue( group.Data.Text );
        }
    }
}
