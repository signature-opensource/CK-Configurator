# CK.IO.AppHost.Desktop

The commands of a desktop application: the contract between its client (CK.Ng.AppHost.Desktop) and its host
(CK.AppHost.Desktop, that handles them).

| Command | Result | What it does |
|---|---|---|
| `IGetDesktopInfoQCommand` | `IDesktopInfo` | Whether the application runs with its window (`HasWindow`) and writes a text log (`HasTextLog`). |
| `IOpenLastLogCommand` | the path of the file | Closes the current text log (the next log line starts a new file) and opens it with the application of the system for ".log" files. |
| `IOpenLogFolderCommand` | the path of the folder | Opens the folder of the text logs with the file manager of the system. |
| `IPickFolderCommand` (`Title`, `Folder`) | the chosen folder, or null | Shows the folder dialog of the system in the window. |

The two "open" commands fail when the application has no window: a server never opens files on its machine.

Like every command of the local endpoint, they can be sent by any process of the machine (the endpoint has no
authentication): see "The installed application" in the README of CK.AppHost.Desktop.
