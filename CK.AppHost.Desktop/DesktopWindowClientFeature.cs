using CK.Core;
using CK.TypeScript;
using Microsoft.Extensions.Configuration;
using System;
using System.Globalization;

namespace CK.AppIdentity;

/// <summary>
/// The "DesktopWindow" feature of a client application: the application is a desktop application whose native window
/// (InfiniFrame) shows this client. The window is run by <see cref="CKomposableDesktopHost"/> (the <c>Program.cs</c> of
/// the host): closing the window stops the host; stopping the host closes the window.
/// <para>
/// Configuration ("Local:PartyClient:{Client}:DesktopWindow"): <c>true</c>, or
/// <c>{ "Title": "...", "Width": 1280, "Height": 800 }</c> (the title defaults to the client name, the size to 1280x800).
/// </para>
/// <para>
/// Only a client of the root party can have it, and only one: a desktop application is one process with one window.
/// The client must be served by the endpoint of the party ("Hosting"): the window shows its <see cref="Url"/>.
/// </para>
/// </summary>
[Requires<HostingClientFeature>]
[PartyClientFeature( "DesktopWindow", TypeActivation.Optional )]
public sealed class DesktopWindowClientFeature : ClientFeature
{
    readonly HttpEndpointFeatureDriver _http;

    internal DesktopWindowClientFeature( PartyClient client,
                                         ImmutableConfigurationSection configuration,
                                         DesktopWindowSettings settings,
                                         HostingClientFeature hosting,
                                         HttpEndpointFeatureDriver http )
        : base( client, configuration )
    {
        Settings = settings;
        Hosting = hosting;
        _http = http;
    }

    /// <summary>
    /// Gets the title and the size of the window.
    /// </summary>
    public DesktopWindowSettings Settings { get; }

    /// <summary>
    /// Gets the "Hosting" feature of the client: the endpoint of the party serves the client at its
    /// <see cref="HostingClientFeature.RequestPath"/>.
    /// </summary>
    public HostingClientFeature Hosting { get; }

    /// <summary>
    /// Gets the url that the window shows: the <see cref="RunningHttpEndpoint.DefaultUrl"/> of the endpoint of the party
    /// (a port 0 is resolved) followed by the request path of the client. Null until the endpoint is started.
    /// </summary>
    public string? Url
    {
        get
        {
            var root = _http.Endpoint?.DefaultUrl;
            if( root == null ) return null;
            var path = Hosting.RequestPath.Trim( '/' );
            return path.Length == 0 ? root.TrimEnd( '/' ) + '/' : $"{root.TrimEnd( '/' )}/{path}/";
        }
    }
}

/// <summary>
/// The title and the size of a desktop window.
/// </summary>
/// <param name="Title">The title of the window.</param>
/// <param name="Width">The width of the window.</param>
/// <param name="Height">The height of the window.</param>
public sealed record DesktopWindowSettings( string Title, int Width, int Height )
{
    /// <summary>
    /// The default width.
    /// </summary>
    public const int DefaultWidth = 1280;

    /// <summary>
    /// The default height.
    /// </summary>
    public const int DefaultHeight = 800;

    /// <summary>
    /// Reads the "DesktopWindow" section of a client: <c>true</c>, or an object with "Title", "Width" and "Height".
    /// </summary>
    /// <param name="section">The "DesktopWindow" section.</param>
    /// <param name="defaultTitle">The title when the section has none (the client name).</param>
    /// <param name="error">The error when null is returned.</param>
    /// <returns>The settings or null on error.</returns>
    public static DesktopWindowSettings? Read( IConfigurationSection section, string defaultTitle, out string? error )
    {
        error = null;
        string? title = null;
        int width = DefaultWidth;
        int height = DefaultHeight;
        foreach( var k in section.GetChildren() )
        {
            if( k.Key.Equals( "Title", StringComparison.OrdinalIgnoreCase ) ) title = k.Value;
            else if( k.Key.Equals( "Width", StringComparison.OrdinalIgnoreCase ) )
            {
                if( !TryReadSize( k.Value, out width ) ) error = $"Invalid \"{k.Path}\": a positive integer is expected.";
            }
            else if( k.Key.Equals( "Height", StringComparison.OrdinalIgnoreCase ) )
            {
                if( !TryReadSize( k.Value, out height ) ) error = $"Invalid \"{k.Path}\": a positive integer is expected.";
            }
            else error = $"Unknown \"{k.Path}\": the keys of \"DesktopWindow\" are Title, Width and Height.";
            if( error != null ) return null;
        }
        return new DesktopWindowSettings( string.IsNullOrWhiteSpace( title ) ? defaultTitle : title, width, height );

        static bool TryReadSize( string? text, out int value )
        {
            return int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out value ) && value > 0;
        }
    }
}

/// <summary>
/// The driver of the <see cref="DesktopWindowClientFeature"/>: only one client of the root party can have it.
/// </summary>
public sealed class DesktopWindowClientFeatureDriver : ClientFeatureDriver<DesktopWindowClientFeature>
{
    readonly ApplicationIdentityService _root;
    readonly HttpEndpointFeatureDriver _http;
    DesktopWindowClientFeature? _feature;

    /// <summary>
    /// Initializes a new driver.
    /// </summary>
    /// <param name="root">The application identity service: the root party.</param>
    /// <param name="http">The "HttpEndpoint" feature (required by "Hosting"): the window shows its endpoint.</param>
    public DesktopWindowClientFeatureDriver( ApplicationIdentityService root, HttpEndpointFeatureDriver http )
    {
        _root = root;
        _http = http;
    }

    /// <inheritdoc />
    protected override DesktopWindowClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration )
    {
        if( client.Party != _root )
        {
            monitor.Error( $"The {client} declares \"DesktopWindow\": only a client of the root party can have a desktop window." );
            return null;
        }
        if( _feature != null )
        {
            monitor.Error( $"The {client} declares \"DesktopWindow\" but the {_feature.Client} has it: a desktop application has one window." );
            return null;
        }
        var settings = DesktopWindowSettings.Read( configuration, client.Name, out var error );
        if( settings == null )
        {
            monitor.Error( error! );
            return null;
        }
        // The requirements are checked: "Hosting" is here.
        var hosting = client.GetFeature<HostingClientFeature>();
        Throw.CheckState( hosting != null );
        return _feature = new DesktopWindowClientFeature( client, configuration, settings, hosting, _http );
    }
}
