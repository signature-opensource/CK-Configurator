# CK-Configurator

The configurator: an application that edits the Unified Configuration of a CKomposable solution.

| Project | |
|---|---|
| [CK.Configurator.Catalog](CK.Configurator.Catalog/README.md) | The catalog of what can be configured: the analyses of the packages of a published profile, their cache and the worker that computes them. |
| [CK.Configurator.Catalog.Worker](CK.Configurator.Catalog.Worker/README.md) | The analyzer that runs in the worker of a source. |
| [CK.IO.Configurator](CK.IO.Configurator/README.md) | The commands of the configurator (the catalog, the editor). |
| [CK.Configurator](CK.Configurator/README.md) | The backend: the command handlers and the opened catalogs. |
| [CK.Ng.Configurator](CK.Ng.Configurator/README.md) | The Angular side: the opened catalog (`CatalogSource`), its picker and selector, the editor page (`ckomposable/editor`) and the catalog page (`ckomposable/catalog`). |

## The configurator application

`Configurator/` holds the application itself, the party `CK/$Configurator` (Configurator.App, Configurator.Builder,
Configurator.Host), configured by the central [ckonf.json](ckonf.json). It is a web application: its "HttpEndpoint"
on `http://localhost:5400` serves its client application (the editor and the catalog pages) in a browser, with the
live reload of the client's dev server in Development:

```
dotnet run --project Configurator/Configurator.Host
```

A development build references the analyzer project of this solution ("Configurator:AnalyzerProject").

Its deployment, [Deployment/#Production](Deployment/%23Production/ckonf.json), packages it as a desktop application: it
adds [CK.Ng.AppHost.DesktopNgApp](https://github.com/signature-opensource/CK-AppHost-Desktop/blob/stable/CK.Ng.AppHost.DesktopNgApp/README.md)
to the App (its "DesktopNgApp" aspect: a window shows the client, the endpoint is in process, no port is opened), as
one executable (win-x64, self-contained, the native libraries inside it) with its client application next to it. Tested
on Windows. CKli builds it after the build of
the repository (`Deployment/GenerateAssets.cs`, CK.AppDeployment): `Deployment/Assets/Production-Configurator`.

