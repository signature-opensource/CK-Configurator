# CK.Configurator

The backend of the configurator: it handles the commands of [CK.IO.Configurator](../CK.IO.Configurator/README.md) with
the catalogs of [CK.Configurator.Catalog](../CK.Configurator.Catalog/README.md).

- `ConfiguratorCatalogService` (a singleton) keeps the opened catalogs by their source: `ConfiguratorCatalogService.OpenAsync`
  resolves the source, analyzes what is missing and loads the catalog once; `OpenedCatalog.GetServerAsync` starts its
  worker server on the first request that needs it (a closure). The workers stop when the service is disposed. Its
  "Configurator" section is read at start:
  `"Configurator": { "CacheRoot": "...", "AnalyzerProject": "CK.Configurator.Catalog.Worker/CK.Configurator.Catalog.Worker.csproj" }`.
  The cache defaults to `CatalogCache.DefaultRoot`; the analyzer defaults to the CK.Configurator.Catalog.Worker package
  of the version of CK.Configurator.Catalog: a development build (the zero version) configures the worker project
  (absolute or relative to the folder of the development solution).
- `ConfiguratorCatalogHandlers` maps the catalog to the result Pocos. A catalog that can't be opened is an error result
  (the details are in the logs). No right is checked (see CK.IO.Configurator).
