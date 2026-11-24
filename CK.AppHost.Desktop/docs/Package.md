# CK.AppHost.Desktop

A CKomposable application as a desktop application: a native window (InfiniFrame) shows its client application, and
closing the window stops the host.

The "DesktopWindow" feature of a client application (`DesktopWindowClientFeature`) and the bootstrap of its host
(`CKomposableDesktopHost.Run`); a failure of the host is shown in the window. With an in-process endpoint (the
"InProcess" option of CK.AppIdentity.HttpEndpoint.InProcess), no port is opened: the window relays the requests of its
page (`app://localhost`).

See the [README](https://github.com/signature-opensource/CK-Configurator/blob/stable/CK.AppHost.Desktop/README.md).
