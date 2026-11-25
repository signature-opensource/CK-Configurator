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
[Configuration<DesktopWindowConfiguration>]
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
    /// Looks at the "DesktopWindow" section of a client before the host starts (the window opens at once): the "Title",
    /// "Width" and "Height" when they are valid, the defaults otherwise. The configuration model of the feature
    /// (<see cref="DesktopWindowConfiguration"/>) checks the section when the host starts.
    /// </summary>
    /// <param name="section">The "DesktopWindow" section.</param>
    /// <param name="defaultTitle">The title when the section has none (the client name).</param>
    /// <returns>The settings of the window.</returns>
    public static DesktopWindowSettings Peek( IConfigurationSection section, string defaultTitle )
    {
        var title = section["Title"];
        return new DesktopWindowSettings( string.IsNullOrWhiteSpace( title ) ? defaultTitle : title,
                                          Size( section["Width"], DefaultWidth ),
                                          Size( section["Height"], DefaultHeight ) );

        static int Size( string? text, int defaultValue )
            => int.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var v ) && v > 0 ? v : defaultValue;
    }
}

/// <summary>
/// The configuration of the "DesktopWindow" feature of a client: <c>true</c>, or its title and its size.
/// </summary>
[ConfigurationModel]
public sealed class DesktopWindowConfiguration : IConfigurationModel
{
    /// <summary>
    /// Gets the title of the window (the client name by default).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the width of the window.
    /// </summary>
    public int Width { get; init; } = DesktopWindowSettings.DefaultWidth;

    /// <summary>
    /// Gets the height of the window.
    /// </summary>
    public int Height { get; init; } = DesktopWindowSettings.DefaultHeight;

    /// <inheritdoc />
    public void Validate( IConfigurationValidationContext context )
    {
        if( Width <= 0 ) context.Error( $"'{context.Path}:Width' is {Width}: a positive integer is expected." );
        if( Height <= 0 ) context.Error( $"'{context.Path}:Height' is {Height}: a positive integer is expected." );
    }
}

/// <summary>
/// The driver of the <see cref="DesktopWindowClientFeature"/>: only one client of the root party can have it.
/// </summary>
public sealed class DesktopWindowClientFeatureDriver : ClientFeatureDriver<DesktopWindowClientFeature, DesktopWindowConfiguration>
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
    protected override DesktopWindowClientFeature? Create( IActivityMonitor monitor, PartyClient client, ImmutableConfigurationSection configuration, DesktopWindowConfiguration model )
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
        var settings = new DesktopWindowSettings( string.IsNullOrWhiteSpace( model.Title ) ? client.Name : model.Title, model.Width, model.Height );
        // The requirements are checked: "Hosting" is here.
        var hosting = client.GetFeature<HostingClientFeature>();
        Throw.CheckState( hosting != null );
        return _feature = new DesktopWindowClientFeature( client, configuration, settings, hosting, _http );
    }
}
