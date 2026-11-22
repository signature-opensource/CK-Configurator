# CK-Configurator

The configurator: an application that edits the Unified Configuration of a CKomposable solution.

| Project | |
|---|---|
| [CK.Configurator.Catalog](CK.Configurator.Catalog/README.md) | The catalog of what can be configured: the analyses of the packages of a published profile, their cache and the worker that computes them. |
| [CK.Configurator.Catalog.Worker](CK.Configurator.Catalog.Worker/README.md) | The analyzer that runs in the worker of a source. |
| [CK.IO.Configurator](CK.IO.Configurator/README.md) | The commands of the configurator (the catalog). |
| [CK.Configurator](CK.Configurator/README.md) | The backend: the command handlers and the opened catalogs. |
| [CK.Ng.Configurator](CK.Ng.Configurator/README.md) | The Angular side: the catalog page. |
| [CK.AppHost.Desktop](CK.AppHost.Desktop/README.md) | The "DesktopWindow" feature: a CKomposable application in a native window. |

## The configurator application

`Configurator/` holds the application itself, the party `CK/$Configurator` (Configurator.App, Configurator.Builder,
Configurator.Host), configured by the central [ckonf.json](ckonf.json): its "HttpEndpoint" on a random local port, its
client application (the catalog page) and its "DesktopWindow". It runs as a desktop application (Windows):

```
dotnet run --project Configurator/Configurator.Host
```

A development build references the analyzer project of this solution ("Configurator:AnalyzerProject").

