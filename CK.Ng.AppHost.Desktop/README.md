# CK.Ng.AppHost.Desktop

The Angular side of a desktop application: the commands of CK.IO.AppHost.Desktop and the component that sends them.

## `ck-desktop-log-links`

Two links, for the header of an application:

- **Last log** closes the current text log of the application and opens it with the application of the system for
  ".log" files. GrandOutput closes the current file (`T-….log.tmp` becomes its final `….log`) and the next log line
  starts a new one: nothing is copied, nothing is left behind.
- **Open folder** opens the folder of the text logs with the file manager of the system.

The component asks the backend first (`IGetDesktopInfoQCommand`): it shows nothing when the application has no window
(a server, a deployment without "DesktopWindow") or no text log, and when the backend doesn't handle the command. An
error of a link is shown as the title of the links (they turn red).

The backend is CK.AppHost.Desktop: the App references it (it holds the "DesktopWindow" feature too).
