using CK.Core;
using CK.Cris;

namespace CK.IO.AppHost.Desktop;

/// <summary>
/// Gets what the desktop application offers to its client: whether it runs with its window, and whether it writes a
/// text log. A client shows the links of the desktop (the logs) only when <see cref="IDesktopInfo.HasWindow"/> is true.
/// </summary>
public interface IGetDesktopInfoQCommand : ICommand<IDesktopInfo>
{
}

/// <summary>
/// What the desktop application offers.
/// </summary>
public interface IDesktopInfo : IPoco
{
    /// <summary>
    /// Gets or sets whether the application runs with its window (a client of its root party has the "DesktopWindow"
    /// feature). The other commands of the desktop fail otherwise: a server never opens files on its machine.
    /// </summary>
    bool HasWindow { get; set; }

    /// <summary>
    /// Gets or sets whether the application writes a text log (a "TextFile" handler of its GrandOutput).
    /// </summary>
    bool HasTextLog { get; set; }
}

/// <summary>
/// Opens the last text log: the current log file is closed (the next log line starts a new one) and opened with the
/// application of the system for ".log" files. Returns the path of the opened file.
/// </summary>
public interface IOpenLastLogCommand : ICommand<string>
{
}

/// <summary>
/// Opens the folder of the text logs with the file manager of the system. Returns the path of the opened folder.
/// </summary>
public interface IOpenLogFolderCommand : ICommand<string>
{
}

/// <summary>
/// Shows the folder dialog of the system in the window. Returns the chosen folder, or null when the dialog is cancelled.
/// </summary>
public interface IPickFolderCommand : ICommand<string?>
{
    /// <summary>
    /// Gets or sets the title of the dialog.
    /// </summary>
    string? Title { get; set; }

    /// <summary>
    /// Gets or sets the folder that the dialog shows first.
    /// </summary>
    string? Folder { get; set; }
}
