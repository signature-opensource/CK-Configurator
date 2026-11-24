# CK.AppHost.Desktop

A CKomposable application as a desktop application: a native window shows one of its client applications. A desktop
application is one process with one window, deployed on its own (an installer): it embeds its host.

It takes two things:

- the "DesktopWindow" feature on a client of the root party (`DesktopWindowClientFeature`), in the App;
- the bootstrap of the host, in its `Program.cs`:
  ```csharp
  return CK.Core.CKomposableDesktopHost.Run( args );
  ```
  It is synchronous: on macOS the window must run on the main thread of the process (an `async` `Main` continues on
  other threads).

## The feature

"DesktopWindow" is an optional client feature that requires "Hosting" (CK.TS.AspNet.Clients): the window shows the
client where the endpoint of the party serves it. It is configured in the section of the client:

```jsonc
"Local": {
  // A random port: the window shows the actual one.
  "HttpEndpoint": { "Urls": "http://127.0.0.1:0" },
  "PartyClient": {
    "App": {
      "NgApp": true,
      "Hosting": { "RequestPath": "/" },
      "DesktopWindow": { "Title": "Configurator", "Width": 1280, "Height": 800 }   // or: true
    }
  }
}
```

- The title defaults to the client name, the size to 1280x800. An invalid value or an unknown key is a setup error.
- Only a client of the root party can have it, and only one client: a second one is a setup error.
- `DesktopWindowClientFeature.Url` is the url of the window: the `RunningHttpEndpoint.DefaultUrl` of the endpoint (a
  port 0 is resolved) followed by the `RequestPath` of the client. It is null until the endpoint is started.

## The bootstrap

`CKomposableDesktopHost.Run` builds the host (`CKomposableAppHost.Build`), then reads the configuration: when a client
of the root party has "DesktopWindow", the application runs with its window, otherwise the host runs as with
`CKomposableAppHost.RunAsync`. With the window:

- The window opens at once on a waiting page, the host starts in the background.
- Once the host is started, the window loads the url of the client when it answers without a server error (in
  Development, a client is served by its dev server that compiles before it answers; after 3 minutes the url is loaded
  anyway). The origin of the endpoint is trusted then (InfiniFrame only navigates to a trusted origin).
- A failure is shown in the window: the errors logged while the host started (the actual causes: a setup error only
  says that a feature failed), where the logs are, and the exception. The window stays open until it is closed. A
  failure to build the host (before anything is configured) opens a window that shows it, when a window can be opened.
- Closing the window stops the host; stopping the host closes the window.
- The exit code is 0, or 1 on failure.

The window is an [InfiniFrame](https://github.com/InfiniLore/InfiniFrame) window. It runs on the main thread, except on
Windows where it runs on an STA thread of its own (WebView2 requires an STA thread, and the main thread of a top-level
`Program.cs` is not one). **Tested on Windows (WebView2) only:** macOS (WKWebView) and Linux (WebKitGTK) are untested.

## The installed application

What a desktop application needs once it is installed (not done here, see the distribution of the configurator):

- An executable without console: `<OutputType>WinExe</OutputType>` on Windows. The console logs are off in its
  deployment (the "Console" handler of the GrandOutput), its logs go to files in a folder where it can write.
- The logs and the files it writes are in the user's application data (`%LOCALAPPDATA%` and its macOS and Linux
  equivalents), not next to the executable.
- Its endpoint is open to every process of the machine (`127.0.0.1`): the protection of the endpoint (a secret given to
  the window only, or a custom scheme instead of a TCP port) is to be decided.
- Two launches are two independent applications: what they share on disk must be safe under concurrency.

## Where it is

The package holds the App side (the feature) and the host side (the bootstrap): it references CK.AppHost for its code
only (`ExcludeAssets="build"`: the build targets of CK.AppHost are the conventions of a host project).
