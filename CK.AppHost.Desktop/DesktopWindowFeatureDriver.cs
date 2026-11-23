using CK.Core;
using InfiniFrame;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CK.AppIdentity;

/// <summary>
/// The "DesktopWindow" feature of the root party: once the host is started, a native window (InfiniFrame) shows the
/// endpoint of its "HttpEndpoint" feature (its <see cref="RunningHttpEndpoint.DefaultUrl"/>: a port 0 is resolved).
/// Closing the window stops the host; stopping the host closes the window.
/// <para>
/// The window first shows a waiting page and loads the endpoint once it answers without a server error (in Development, a
/// client application is served by its dev server that compiles before it answers).
/// </para>
/// <para>
/// Configuration ("Local:DesktopWindow"): <c>true</c>, or <c>{ "Title": "...", "Width": 1280, "Height": 800 }</c> (the
/// title defaults to the party name, the size to 1280x800).
/// </para>
/// <para>
/// The window runs on a thread of its own (an STA thread with its message loop): the main thread of the process stays
/// the host's. This is supported on Windows (WebView2) only: elsewhere the feature is a setup error.
/// </para>
/// </summary>
[PartyFeature( "DesktopWindow", TypeActivation.Optional )]
public sealed class DesktopWindowFeatureDriver : ApplicationIdentityStaticFeatureDriver
{
    /// <summary>
    /// The configuration key of the feature, in the "Local" section.
    /// </summary>
    public const string ConfigurationKey = "DesktopWindow";

    readonly HttpEndpointFeatureDriver _http;
    readonly IServiceProvider _services;
    WindowConfiguration? _configuration;
    Thread? _thread;
    volatile IInfiniFrameWindow? _window;
    readonly TaskCompletionSource _closed;

    sealed record WindowConfiguration( string Title, int Width, int Height );

    /// <summary>
    /// Initializes a new <see cref="DesktopWindowFeatureDriver"/>.
    /// </summary>
    /// <param name="s">The application identity service.</param>
    /// <param name="http">The "HttpEndpoint" feature: the window shows its endpoint (it starts before this one).</param>
    /// <param name="services">The services of the host.</param>
    public DesktopWindowFeatureDriver( ApplicationIdentityService s, HttpEndpointFeatureDriver http, IServiceProvider services )
        : base( s )
    {
        _http = http;
        _services = services;
        _closed = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    /// <summary>
    /// Gets whether the window is open.
    /// </summary>
    public bool IsOpen => _window is { } w && !w.IsClosedOrClosing();

    /// <summary>
    /// Gets a task that completes when the window is closed (immediately completed when the feature has no window).
    /// </summary>
    public Task Closed => _closed.Task;

    /// <inheritdoc />
    protected override Task<bool> SetupAsync( FeatureLifetimeContext context )
    {
        var monitor = context.Monitor;
        bool success = true;
        foreach( var local in context.GetAllLocals() )
        {
            if( local != ApplicationIdentityService && IsAllowedFeature( local ) )
            {
                monitor.Error( $"'{local}' declares the '{FeatureName}' feature: only the root party can have a desktop window." );
                success = false;
            }
        }
        if( !IsRootAllowed )
        {
            _closed.TrySetResult();
            return Task.FromResult( success );
        }
        if( !OperatingSystem.IsWindows() )
        {
            monitor.Error( $"The '{FeatureName}' feature is only supported on Windows." );
            return Task.FromResult( false );
        }
        if( !_http.IsRootAllowed )
        {
            monitor.Error( $"The '{FeatureName}' feature shows the endpoint of the '{_http.FeatureName}' feature: it must be declared too." );
            return Task.FromResult( false );
        }
        _configuration = ReadConfiguration( monitor );
        return Task.FromResult( success && _configuration != null );
    }

    WindowConfiguration? ReadConfiguration( IActivityMonitor monitor )
    {
        var section = ApplicationIdentityService.LocalConfiguration.Configuration.TryGetSection( ConfigurationKey );
        var title = section?["Title"] ?? ApplicationIdentityService.PartyName;
        if( !TryReadSize( section?["Width"], 1280, out var width ) || !TryReadSize( section?["Height"], 800, out var height ) )
        {
            monitor.Error( $"Invalid \"Local:{ConfigurationKey}\": \"Width\" and \"Height\" must be positive integers." );
            return null;
        }
        return new WindowConfiguration( title, width, height );

        static bool TryReadSize( string? text, int defaultValue, out int value )
        {
            if( text == null )
            {
                value = defaultValue;
                return true;
            }
            return int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out value ) && value > 0;
        }
    }

    /// <inheritdoc />
    protected override Task OnHostStartedAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var c = _configuration;
        if( c == null ) return Task.CompletedTask;
        var url = _http.Endpoint?.DefaultUrl;
        if( url == null )
        {
            monitor.Error( $"The '{FeatureName}' feature has no URL to show: the endpoint of the '{_http.FeatureName}' feature is not started." );
            _closed.TrySetResult();
            return Task.CompletedTask;
        }
        if( !OperatingSystem.IsWindows() ) return Task.CompletedTask;
        var lifetime = _services.GetService<IHostApplicationLifetime>();
        var started = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        _thread = new Thread( () => RunWindow( c, url, lifetime, started ) ) { Name = "DesktopWindow", IsBackground = true };
        _thread.SetApartmentState( ApartmentState.STA );
        _thread.Start();
        monitor.Info( $"Desktop window opened on '{url}'." );
        return started.Task;
    }

    // The thread of the window: it builds the window, runs its message loop until it is closed, then stops the host.
    void RunWindow( WindowConfiguration c, string url, IHostApplicationLifetime? lifetime, TaskCompletionSource started )
    {
        try
        {
            // The window opens on a waiting page: what the endpoint serves may not answer yet (in Development, a client
            // application is served by its dev server, that compiles before it answers).
            var window = InfiniFrameWindowBuilder.Create()
                            .SetTitle( c.Title )
                            .SetSize( c.Width, c.Height )
                            .SetStartPageContent( WaitingPage( c.Title ) )
                            .Build();
            _window = window;
            started.TrySetResult();
            _ = LoadWhenReadyAsync( window, url );
            window.WaitForClose();
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"The desktop window failed.", ex );
            started.TrySetException( ex );
        }
        finally
        {
            // Closing the window stops the host (does nothing when the host is already stopping), then the window is
            // known to be closed.
            lifetime?.StopApplication();
            _closed.TrySetResult();
        }
    }

    // Loads the url in the window once it answers (anything but a server error: a forwarder answers 502 while the dev
    // server is not listening). After the delay, the url is loaded anyway: the window shows what it answers.
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
            if( !window.IsClosedOrClosing() ) window.Load( url );
        }
        catch( Exception ex )
        {
            ActivityMonitor.StaticLogger.Error( $"While loading '{url}' in the desktop window.", ex );
        }
    }

    static readonly TimeSpan _readyDelay = TimeSpan.FromMinutes( 3 );

    static string WaitingPage( string title )
    {
        var t = WebUtility.HtmlEncode( title );
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><title>{{t}}</title>
            <style>body{font-family:Segoe UI,sans-serif;display:flex;height:100vh;margin:0;align-items:center;justify-content:center;color:#555}</style>
            </head><body><div>{{t}}: starting...</div></body></html>
            """;
    }

    // What a user does: closes the window (the tests).
    internal void CloseWindow() => _window?.Close();

    /// <inheritdoc />
    protected override Task TeardownAsync( FeatureLifetimeContext context )
    {
        // A window still open (OnHostStoppingAsync has not been called) is closed.
        var w = _window;
        if( w != null && !w.IsClosedOrClosing() ) w.Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnHostStoppingAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var w = _window;
        if( w != null && !w.IsClosedOrClosing() )
        {
            w.Close();
            monitor.Info( "Desktop window closed: the host stops." );
        }
        try
        {
            await _closed.Task.WaitAsync( TimeSpan.FromSeconds( 5 ), cancel ).ConfigureAwait( false );
        }
        catch( TimeoutException )
        {
            monitor.Warn( "The desktop window didn't close in 5 seconds." );
        }
    }
}
