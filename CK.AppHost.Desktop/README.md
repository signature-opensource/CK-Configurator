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

- Its configuration model is `DesktopWindowConfiguration` (its driver is a `ClientFeatureDriver<T, TModel>`): the title
  defaults to the client name, the size to 1280x800. An invalid value or an unknown key is a setup error, shown in the
  window: the window opens before (`DesktopWindowSettings.Peek` looks at the section: the defaults for what is invalid).
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
- The browser's context menu (Back, Reload, Save as...) is off in a published host (its `CK.AppHost.Published.json`
  is next to it): the client shows its own menus. In Development, it is kept (Inspect).
- The exit code is 0, or 1 on failure.

## The in-process endpoint: no port

With the "InProcess" option of the endpoint (`"HttpEndpoint": { "InProcess": true }`, from
[CK.AppIdentity.HttpEndpoint.InProcess](https://github.com/signature-opensource/CK-AspNet/blob/stable/CK.AppIdentity.HttpEndpoint.InProcess/README.md)),
the web application of the party runs on an in-memory server: no port is opened, nothing outside the window can call
it. The url of the window is then `app://localhost/` (followed by the `RequestPath` of the client), and the window
relays the requests of its page ([`DesktopInProcessBridge`](DesktopInProcessBridge.cs)):

- **The files of the page** (the GET of the `app` scheme) are asked to the in-memory server. `app` is the only custom
  scheme that InfiniFrame registers as a secure origin with an authority (`app://localhost`): any other name gives the
  page a `null` origin (no `fetch`, no `localStorage`). InfiniFrame's handler receives the url only and returns a body
  and a content type: the request has the `Accept` of a navigation for a document (no extension or `.html`: a dev server
  serves its index to it), an error is a text (a response without content type would be a download for the window).
- **The other requests** (the API: Cris) are messages: the axios adapter of
  [CK.Ng.AppHost.Desktop](../CK.Ng.AppHost.Desktop/README.md) posts `{ id, method, url, headers, body }` (envelope id
  `"ck:http"`), the bridge runs it and answers `{ id, status, headers, body, base64 }` (a text or JSON body as is, any
  other as base64). The requests have `Origin` (the url of the server) and `Sec-Fetch-Site: same-origin`: only the
  page of the window sends them. The cookies that the server sets are kept by the bridge.
- **The sizes:** InfiniFrame caps a message from the page at 1 MiB (a request); the responses are read without its
  dispatcher (no cap).
- The window waits for the page to answer without a server error before loading it, as with Kestrel.

In Development the endpoint can stay on Kestrel ("Urls"): the dev server's live reload needs a websocket that the `app`
scheme doesn't carry.

The window is an [InfiniFrame](https://github.com/InfiniLore/InfiniFrame) window. It runs on the main thread, except on
Windows where it runs on an STA thread of its own (WebView2 requires an STA thread, and the main thread of a top-level
`Program.cs` is not one). **Tested on Windows (WebView2) only:** macOS (WKWebView) and Linux (WebKitGTK) are untested.

## The commands of the desktop

`DesktopCommandHandlers` handles the commands of [CK.IO.AppHost.Desktop](../CK.IO.AppHost.Desktop/README.md) (their
client side is [CK.Ng.AppHost.Desktop](../CK.Ng.AppHost.Desktop/README.md)):

- `IGetDesktopInfoQCommand`: whether a client of the root party has the window, and whether the GrandOutput has a
  "TextFile" handler.
- `IOpenLastLogCommand`: a `GrandOutputHandlersAction` closes the current file of the "TextFile" handler (on the
  dispatcher of the GrandOutput, after the entries already sent: the file always contains the command's own log line).
  `T-….log.tmp` becomes its final `….log` and the next log line starts a new file: nothing is copied. The closed file
  is opened with the application of the system for ".log" files.
- `IOpenLogFolderCommand`: opens the folder of the "TextFile" handler (under `LogFile.RootLogPath`).
- `IPickFolderCommand`: shows the folder dialog of the system in the window (InfiniFrame's `FilePickerDialogs`); the
  window is the one that the bootstrap opened (`CKomposableDesktopHost`). It fails without the window.

Files are opened with the shell on Windows, `open` on macOS, `xdg-open` on Linux. The two "open" commands fail without
the window: a server never opens files on its machine.

## The installed application

What a desktop application needs once it is installed:

- An executable without console: `<OutputType>WinExe</OutputType>` in its host project. The console logs are off in
  its deployment (`"Console": false` in the handlers of the GrandOutput): its logs are files.
- **The logs of a published host** (`CK.AppHost.Published.json` next to the executable) are in the user's application
  data: `CKomposableDesktopHost.Run` sets `LogFile.RootLogPath` to `{LocalApplicationData}/{Domain}-{Party}/Logs` (from
  the full name of its deployment: "CK/$Configurator/#Production" gives "CK-Configurator"; the name of the host without
  ".Host" otherwise), unless it is already set. Its folder may be read only (an installed application) or temporary (the
  extraction folder of a single file application). The other files that it writes are in the user's application data
  too, never next to the executable.
- Its endpoint is in process ("InProcess", see above): no port is open to the other processes of the machine.
- Two launches are two independent applications: what they share on disk must be safe under concurrency.

## Where it is

The package holds the App side (the feature) and the host side (the bootstrap): it references CK.AppHost for its code
only (`ExcludeAssets="build"`: the build targets of CK.AppHost are the conventions of a host project).
