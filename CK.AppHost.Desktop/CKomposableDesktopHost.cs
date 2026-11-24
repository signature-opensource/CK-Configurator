using CK.AppIdentity;
using InfiniFrame;
using InfiniFrame.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            return DesktopWindowSettings.Read( w, c.Key, out _ )
                   ?? new DesktopWindowSettings( w["Title"] is { Length: > 0 } t ? t : c.Key,
                                                 DesktopWindowSettings.DefaultWidth,
                                                 DesktopWindowSettings.DefaultHeight );
        }
        return null;
    }

    static int RunWithWindow( IHost host, DesktopWindowSettings settings )
    {
        int exitCode = 0;
        Task? hostTask = null;
        void RunWindow()
        {
            var window = InfiniFrameWindowBuilder.Create()
                            .SetTitle( settings.Title )
                            .SetSize( settings.Width, settings.Height )
                            .SetStartPageContent( Page( settings.Title, "starting...", null ) )
                            .Build();
            hostTask = Task.Run( async () =>
            {
                if( !await RunHostAsync( host, window, settings ).ConfigureAwait( false ) ) Interlocked.Exchange( ref exitCode, 1 );
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
    static async Task<bool> RunHostAsync( IHost host, IInfiniFrameWindow window, DesktopWindowSettings settings )
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
            await LoadWhenReadyAsync( window, url ).ConfigureAwait( false );
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
    static async Task LoadWhenReadyAsync( IInfiniFrameWindow window, string url )
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds( 10 ) };
            var limit = DateTime.UtcNow + _readyDelay;
            while( !window.IsClosedOrClosing() && DateTime.UtcNow < limit )
            {
                try
                {
                    using var response = await http.GetAsync( url, HttpCompletionOption.ResponseHeadersRead ).ConfigureAwait( false );
                    if( (int)response.StatusCode < 500 ) break;
                }
                catch( HttpRequestException ) { }
                catch( TaskCanceledException ) { }
                await Task.Delay( 500 ).ConfigureAwait( false );
            }
            if( window.IsClosedOrClosing() ) return;
            // InfiniFrame only navigates to a trusted origin (a refused navigation is silent): the window opened on a page
            // without origin, the one of the endpoint (its port is known once it is started) is trusted now.
            var uri = new Uri( url );
            var policy = InfiniFrameUriSecurityPolicyRegistry.GetForWindow( window );
            InfiniFrameUriSecurityPolicyRegistry.BindToWindow( window, new InfiniFrameUriSecurityPolicyBuilder( policy ).AddTrustedOrigin( uri ).Build() );
            window.Load( uri );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"While loading '{url}' in the desktop window.", ex );
        }
    }

    static readonly TimeSpan _readyDelay = TimeSpan.FromMinutes( 3 );

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
    static string Page( string title, string message, Exception? ex, IEnumerable<string>? errors = null )
    {
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
            </head><body><div><div>{{t}}: {{m}}</div>{{list}}{{logs}}{{details}}</div></body></html>
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
