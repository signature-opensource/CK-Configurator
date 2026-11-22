# CK.AppHost.Desktop

A CKomposable application as a desktop application: the "DesktopWindow" feature of a party opens a native window on the
endpoint of its "HttpEndpoint" feature. The application stays a regular CKomposable host (its `Program.cs` is
`await CK.Core.CKomposableAppHost.RunAsync( args );`): referencing this package and declaring the feature is all it
takes.

## The feature

`DesktopWindowFeatureDriver` is the "DesktopWindow" party feature (optional, root party only), configured in the `Local`
section of the party:

```jsonc
"Local": {
  // A random port: the window shows the actual one.
  "HttpEndpoint": { "Urls": "http://127.0.0.1:0" },
  "DesktopWindow": { "Title": "Configurator", "Width": 1280, "Height": 800 }   // or: true
}
```

- The title defaults to the party name, the size to 1280x800. An invalid size is a setup error.
- The "HttpEndpoint" feature must be declared: the window shows its `RunningHttpEndpoint.DefaultUrl` (a port 0 is
  resolved), once the host is started (the feature starts after the endpoint).
- Closing the window stops the host; stopping the host closes the window. `DesktopWindowFeatureDriver.IsOpen` and
  `DesktopWindowFeatureDriver.Closed` tell where it is.

## The window

The window is an [InfiniFrame](https://github.com/InfiniLore/InfiniFrame) window (WebView2 on Windows). It runs on a
thread of its own, an STA thread with its message loop: the main thread of the process stays the host's, so
CK.AppHost is unchanged. This is supported on Windows only: elsewhere the feature is a setup error (macOS requires the
window on the main thread).
