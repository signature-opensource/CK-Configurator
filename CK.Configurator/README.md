# CK.Configurator

The backend of the configurator: it handles the commands of [CK.IO.Configurator](../CK.IO.Configurator/README.md) with
the catalogs of [CK.Configurator.Catalog](../CK.Configurator.Catalog/README.md).

- `ConfiguratorCatalogService` (a singleton) keeps the opened catalogs by their source (a World and a profile version):
  `ConfiguratorCatalogService.OpenAsync` resolves the source, analyzes what is missing and loads the catalog once;
  `OpenedCatalog.GetServerAsync` starts its worker server on the first request that needs it (a closure). The workers
  stop when the service is disposed. `ConfiguratorCatalogService.GetWorldsAsync`, `ConfiguratorCatalogService.GetBranchesAsync` and
  `ConfiguratorCatalogService.GetProfilesAsync` list what a World published (the profiles read are kept in the
  `profiles` folder of the cache). Its
  "Configurator" section is read at start:
  `"Configurator": { "CacheRoot": "...", "AnalyzerProject": "CK.Configurator.Catalog.Worker/CK.Configurator.Catalog.Worker.csproj" }`.
  The cache defaults to `CatalogCache.DefaultRoot`; the analyzer defaults to the CK.Configurator.Catalog.Worker package
  of the version of CK.Configurator.Catalog: a development build (the zero version) configures the worker project
  (absolute or relative to the folder of the development solution).
- `ConfiguratorCatalogHandlers` maps the catalog to the result Pocos. A catalog that can't be opened is an error result
  (the details are in the logs). No right is checked (see CK.IO.Configurator).

## The editor

[`ConfiguratorEditorService`](Editor/ConfiguratorEditorService.cs) (a singleton) holds the document of the editor: the
central configuration (`ckonf.json`) of one solution. Its methods return an `EditorSnapshot`, mapped to the `IEditorState`
of every editor command by [`ConfiguratorEditorHandlers`](Editor/ConfiguratorEditorHandlers.cs).

- **The text is edited with the JSON language** of CK.TypeScript.Transform (`JsonCodeEditorExtensions`): an edit
  changes only its tokens, the comments and the layout are kept. The property names are case insensitive, like the
  configuration keys.
- **Undo and redo** keep the texts before the edits (a document is a few kilobytes). A document is dirty when its text
  differs from the saved one: undoing back to it makes it clean.
- **The file is read and written as bytes:** its encoding (UTF-8, with or without BOM) is kept. Its SHA-256 detects a
  change on disk since it was read or saved (an editor, a pull): a save is then refused unless it overwrites, or the
  document is reloaded (its edits are lost).
- **The snapshot lists the properties** in document order, with their path (the names separated by `:`), their depth,
  their kind, the JSON text of a value that is not an object, and the comments written on the lines above them (the
  documentation that people read). The `"$configurator"` root key gives the source of the catalog of the solution.
- **The catalog of the document** is the one of its `"$configurator"` root key (`ConfiguratorEditorService.SetCatalogSourceAsync`
  writes it: an edit). When it is opened (`ConfiguratorCatalogService.TryGetOpened`: the editor never opens a catalog,
  this takes a minute), [`ConfigurationChecker`](Editor/ConfigurationChecker.cs) types the properties (their role: a
  builder, a Builder aspect, an App, an App aspect, a party, a party feature, a client, a client feature, an option) and
  checks the document statically, with the rules of the build: the root keys and the keys of a builder, the
  configurable types known by the catalog (a key of a party that is no feature is a warning: the build leaves it to the
  run time), an option under another owner, a required type set to `false`, a configurable type that is not `true`,
  `false` or a section, the syntax of `"$LOCK"`, `"$PER_DEPLOYMENT"` and `{ "$SECRET": "..." }`, a secret written as a
  plain value. Without its catalog, a document has an Info problem that says so. The keys of a section are free.
- The commands are serialized.
