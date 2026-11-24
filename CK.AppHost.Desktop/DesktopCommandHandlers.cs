using CK.AppIdentity;
using CK.Core;
using CK.Cris;
using CK.IO.AppHost.Desktop;
using CK.Monitoring;
using CK.Monitoring.Handlers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CK.AppHost.Desktop;

/// <summary>
/// The handlers of the commands of a desktop application (CK.IO.AppHost.Desktop): the desktop information, the last log
/// and the log folder.
/// </summary>
public sealed class DesktopCommandHandlers : ISingletonAutoService
{
    readonly ApplicationIdentityService _root;

    /// <summary>
    /// Initializes the handlers.
    /// </summary>
    /// <param name="root">The application identity service: whether a client of the root party has the window.</param>
    public DesktopCommandHandlers( ApplicationIdentityService root ) => _root = root;

    bool HasWindow => _root.GetFeature<PartyClientFeature>()?.Clients.Any( c => c.GetFeature<DesktopWindowClientFeature>() != null ) ?? false;

    /// <summary>
    /// Handles <see cref="IGetDesktopInfoQCommand"/>.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The command.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The desktop information.</returns>
    [CommandHandler]
    public async Task<IDesktopInfo> HandleAsync( IActivityMonitor monitor, IGetDesktopInfoQCommand command, PocoDirectory pocos )
    {
        var hasWindow = HasWindow;
        var hasTextLog = hasWindow && await RunAsync( new FindTextLog() ).ConfigureAwait( false ) != null;
        return pocos.Create<IDesktopInfo>( i =>
        {
            i.HasWindow = hasWindow;
            i.HasTextLog = hasTextLog;
        } );
    }

    /// <summary>
    /// Handles <see cref="IPickFolderCommand"/>: shows the folder dialog of the system in the window.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The chosen folder or null.</returns>
    [CommandHandler]
    public async Task<string?> HandleAsync( IPickFolderCommand command )
    {
        var window = CKomposableDesktopHost.Window ?? throw new CKException( "The application has no window: there is no folder dialog." );
        var folders = await window.Features.FilePickerDialogs.ShowOpenFolderAsync( command.Title ?? "", command.Folder ?? "", false, default ).ConfigureAwait( false );
        return folders?.FirstOrDefault( f => !string.IsNullOrEmpty( f ) );
    }

    /// <summary>
    /// Handles <see cref="IOpenLastLogCommand"/>: closes the current text log and opens it.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The command.</param>
    /// <returns>The path of the opened file.</returns>
    [CommandHandler]
    public async Task<string> HandleAsync( IActivityMonitor monitor, IOpenLastLogCommand command )
    {
        CheckWindow();
        // This log line is in the closed file: it is never empty (an empty file is deleted and null is returned).
        monitor.Info( "Opening the last text log." );
        var path = await RunAsync( new CloseTextLog() ).ConfigureAwait( false )
                   ?? throw new CKException( "The application has no text log to open." );
        OpenWithSystem( path );
        return path;
    }

    /// <summary>
    /// Handles <see cref="IOpenLogFolderCommand"/>: opens the folder of the text logs.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The command.</param>
    /// <returns>The path of the opened folder.</returns>
    [CommandHandler]
    public async Task<string> HandleAsync( IActivityMonitor monitor, IOpenLogFolderCommand command )
    {
        CheckWindow();
        var path = await RunAsync( new FindTextLog() ).ConfigureAwait( false )
                   ?? throw new CKException( "The application has no text log." );
        OpenWithSystem( path );
        return path;
    }

    void CheckWindow()
    {
        Throw.CheckState( "Only a desktop application with its window opens files on its machine.", HasWindow );
    }

    // Runs an action on the handlers of the GrandOutput (on its dispatcher: after the log entries already sent).
    static Task<string?> RunAsync( GrandOutputHandlersAction<string?> action )
    {
        var g = GrandOutput.Default;
        if( g == null ) return Task.FromResult<string?>( null );
        g.Sink.Submit( action );
        return action.Completion;
    }

    // The folder of the first "TextFile" handler.
    sealed class FindTextLog : GrandOutputHandlersAction<string?>
    {
        protected override ValueTask<string?> RunAsync( IActivityMonitor monitor, DispatcherSink.HandlerList handlers )
        {
            var t = handlers.Handlers.OfType<TextFile>().FirstOrDefault();
            return ValueTask.FromResult( t == null || LogFile.RootLogPath == null
                                            ? null
                                            : Path.GetFullPath( Path.Combine( LogFile.RootLogPath, t.KeyPath ) ) );
        }
    }

    // Closes the current file of the first "TextFile" handler: its final path, or null when it was empty.
    sealed class CloseTextLog : GrandOutputHandlersAction<string?>
    {
        protected override ValueTask<string?> RunAsync( IActivityMonitor monitor, DispatcherSink.HandlerList handlers )
        {
            var t = handlers.Handlers.OfType<TextFile>().FirstOrDefault();
            return ValueTask.FromResult( t?.CloseCurrentFile() );
        }
    }

    // Opens a file or a folder with the application of the system for it.
    static void OpenWithSystem( string path )
    {
        if( OperatingSystem.IsWindows() )
        {
            Process.Start( new ProcessStartInfo( path ) { UseShellExecute = true } )?.Dispose();
        }
        else
        {
            Process.Start( OperatingSystem.IsMacOS() ? "open" : "xdg-open", [path] )?.Dispose();
        }
    }
}
