# CK-Configurator

The configurator: an application that edits the Unified Configuration of a CKomposable solution.

| Project | |
|---|---|
| [CK.Configurator.Catalog](CK.Configurator.Catalog/README.md) | The catalog of what can be configured: the analyses of the packages of a published profile, their cache and the worker that computes them. |
| [CK.Configurator.Catalog.Worker](CK.Configurator.Catalog.Worker/README.md) | The analyzer that runs in the worker of a source. |
| [CK.IO.Configurator](CK.IO.Configurator/README.md) | The commands of the configurator (the catalog, the editor). |
| [CK.Configurator](CK.Configurator/README.md) | The backend: the command handlers and the opened catalogs. |
| [CK.Ng.Configurator](CK.Ng.Configurator/README.md) | The Angular side: the opened catalog (`CatalogSource`), its picker and selector, the editor page (`ckomposable/editor`) and the catalog page (`ckomposable/catalog`). |
| [CK.IO.AppHost.Desktop](CK.IO.AppHost.Desktop/README.md) | The commands of a desktop application: its information, its last log, its log folder. |
| [CK.AppHost.Desktop](CK.AppHost.Desktop/README.md) | The "DesktopWindow" feature of a client, the bootstrap of a desktop host and the handlers of the desktop commands: a CKomposable application in a native window. |
| [CK.Ng.AppHost.Desktop](CK.Ng.AppHost.Desktop/README.md) | The Angular side of a desktop application: the "Last log" and "Open folder" links. |

## The configurator application

`Configurator/` holds the application itself, the party `CK/$Configurator` (Configurator.App, Configurator.Builder,
Configurator.Host), configured by the central [ckonf.json](ckonf.json): its "HttpEndpoint" in process (no port), its
client application (the editor and the catalog pages) shown in the window of the desktop application (its
"DesktopWindow"; the host's `Program.cs` is `return CK.Core.CKomposableDesktopHost.Run( args );`). Tested on Windows:

```
dotnet run --project Configurator/Configurator.Host
```

A development build references the analyzer project of this solution ("Configurator:AnalyzerProject").

Its deployment, [Deployment/#Production](Deployment/%23Production/ckonf.json), is the desktop application as one
executable (win-x64, self-contained, the client and the native libraries inside it). CKli builds it after the build of
the repository (`Deployment/GenerateAssets.cs`, CK.AppDeployment): `Deployment/Assets/Production-Configurator`.

