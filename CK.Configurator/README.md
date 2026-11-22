# CK.Configurator

The backend of the configurator: it handles the commands of [CK.IO.Configurator](../CK.IO.Configurator/README.md) with
the catalogs of [CK.Configurator.Catalog](../CK.Configurator.Catalog/README.md).

- `ConfiguratorCatalogService` (a singleton) keeps the opened catalogs by their source: `ConfiguratorCatalogService.OpenAsync`
  resolves the source, analyzes what is missing and loads the catalog once; `OpenedCatalog.GetServerAsync` starts its
  worker server on the first request that needs it (a closure). The workers stop when the service is disposed. The host
  can set its `Cache` (`CatalogCache.DefaultRoot` by default) and its `Analyzer`: by default the
  CK.Configurator.Catalog.Worker package of the version of CK.Configurator.Catalog; a development build (the zero
  version) must set it (`AnalyzerReference.FromProject`).
- `ConfiguratorCatalogHandlers` maps the catalog to the result Pocos. A catalog that can't be opened is an error result
  (the details are in the logs). No right is checked (see CK.IO.Configurator).
