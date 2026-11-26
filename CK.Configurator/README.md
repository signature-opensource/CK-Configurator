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
  The cache defaults to `CatalogCache.DefaultRoot`; the analyzer (`ConfiguratorCatalogService.Analyzer`) defaults to
  the CK.Configurator.Catalog.Worker package of the version of CK.Configurator.Catalog: a development build (the zero
  version) configures the worker project (absolute or relative to the folder of the development solution).
- **A profile is analyzed by its own worker** when it has one (`CatalogSource.Worker`: the CK.Configurator.Catalog.Worker
  package of the profile, built with the packages it analyzes), unless the analyzer is a project (a development build:
  the analyzer being developed is used). When the worker of the profile fails, or writes another format of the analyses
  (`CatalogCache.HasOtherFormat`: it is then remembered, `CatalogCache.SetIncompatibleWorker`), the `Analyzer` is used.
- `ConfiguratorCatalogHandlers` maps the catalog to the result Pocos. A catalog that can't be opened is an error result
  (the details are in the logs). No right is checked (see CK.IO.Configurator).

## The editor

[`ConfiguratorEditorService`](Editor/ConfiguratorEditorService.cs) (a singleton) holds the documents of the editor: the
central configuration (`ckonf.json`) of one solution and the overlays of its deployments (`Deployment/#Production/ckonf.json`,
found with `CentralDeployment.DiscoverFolders` of CK.Engine.Configuration). Its methods return an `EditorSnapshot` of a
view, mapped to the `IEditorState` of every editor command by [`ConfiguratorEditorHandlers`](Editor/ConfiguratorEditorHandlers.cs).

- **The text is edited with the JSON language** of CK.TypeScript.Transform (`JsonCodeEditorExtensions`): an edit
  changes only its tokens, the comments and the layout are kept. The property names are case insensitive, like the
  configuration keys.
- **Undo and redo** keep the texts before the edits (a document is a few kilobytes), in one history for the solution:
  an undo changes one document and returns its view. A document is dirty when its text differs from the saved one:
  undoing back to it makes it clean. Creating a deployment (`CreateDeploymentAsync`, its name checked by
  `CentralDeployment.ValidateName`) is an edit too: its overlay is written at the next save.
- **The files are read and written as bytes:** their encoding (UTF-8, with or without BOM) is kept. Their SHA-256
  detects a change on disk since they were read or saved (an editor, a pull): a save (of every dirty document) is then
  refused unless it overwrites, or the solution is reloaded (its edits are lost).
- **The view of a deployment** ([`EditorTree`](Editor/EditorTree.cs)) is its merged configuration: the central one, the
  overlays of its bases (`"$BASE"`), its own overlay (a JSON Merge Patch, case insensitive), each property with its
  origin ("Central", a base, "Deployment" when its overlay writes it, "Removed" for a `null`). Its edits are written in
  its overlay: a value with the objects of its path that the overlay doesn't have, "Remove here" (a `null`, or the
  removal of what only the overlay writes), "Revert to inherited" (the removal from the overlay, and of the objects that
  become an empty `{}` where an object is inherited).
- **The snapshot lists the properties** in document order, with their path (the names separated by `:`), their depth,
  their kind, the JSON text of a value that is not an object, and the comments written on the lines above them (the
  documentation that people read). The `"$configurator"` root key gives the source of the catalog of the solution.
- **The catalog of the document** is the one of its `"$configurator"` root key (`ConfiguratorEditorService.SetCatalogSourceAsync`
  writes it: an edit). When it is opened (`ConfiguratorCatalogService.TryGetOpened`: the editor never opens a catalog,
  this takes a minute), [`ConfigurationChecker`](Editor/ConfigurationChecker.cs) types the properties (their role: a
  AppContext, an AppContext aspect, an App, an App aspect, a party, a party feature, a client, a client feature, an option) and
  checks the document statically, with the rules of the build: the root keys and the keys of an AppContext, the
  configurable types known by the catalog (a key of a party that is no feature is a warning: the build leaves it to the
  run time), an option under another owner, a required type set to `false`, a configurable type that is not `true`,
  `false` or a section, the syntax of `"$LOCK"`, `"$PER_DEPLOYMENT"` and `{ "$SECRET": "..." }`, a secret written as a
  plain value, and the required types of the options ("only with it": an explicit option that requires a disabled type
  is an error, an option enabled by default is disabled with it, an Info). The view of a deployment is checked the same
  way, then by [`DeploymentChecker`](Editor/DeploymentChecker.cs): the keys that an overlay can't write, its `"$BASE"`,
  the builders of its `"Deployment"` settings, and the values per deployment that it inherits
  (`CentralDeployment.FindInheritedValues`: the `"$PER_DEPLOYMENT"` markers and the `[ConfigurationPerDeployment]` of the
  types of the catalog on the local party, its defaults and its clients), one problem per property that an overlay
  writes as a whole (an array, not its items), with the fix `EditorProblemData.WriteHereFix` (its inherited value
  written in the overlay). A deployment can be created with them written (`CreateDeploymentAsync`'s
  `writeValuesPerDeployment`). Without its catalog, a document has an Info
  problem that says so. The keys of a section are free.
- **The development secrets** (`#Dev/Secrets.json`) are a document of the solution (not a view: their values are never
  shown): `MakeSecretAsync` moves a value of the central configuration there and writes its handle
  (`{ "$SECRET": "<name>" }`, or `"$SECRET?"`), one edit of the two documents; a secret of the same name with another
  value is replaced only when asked. In a deployment, only the handle is written in its overlay (its host is given the
  secret).
- **The locks** are edited in the central configuration: `LockPropertyAsync` writes an entry of the `"$LOCK"` of the App
  or of the AppContext aspect that contains the property (its path relative to it as the pattern), up to the capability of
  the code; `UnlockPropertyAsync` removes the literal pattern that names it (and the `"$LOCK"` that becomes empty).
  [`EditorAnnotations`](Editor/EditorAnnotations.cs) gives each property of a view its lock (the lowest level of the
  `"$LOCK"` patterns that cover it, a `"$LOCK"` of `Defaults:App` or of the `Defaults` of an App applying like the build
  applies it, and the `[ConfigurationLock]` of the types of the catalog, placed on the anchors by
  [`EditorAnchors`](Editor/EditorAnchors.cs) like the values per deployment), its capability (`[ConfigurationLevel]`)
  and its secret (the name of a handle, and whether the development secrets have it). A literal pattern that names no
  property (the build burns its absence) is a property of kind "Absent" at its path, so that the lock is visible.
- **A modeled section** (a configurable type that has a configuration model: its JSON Schema in the catalog) is checked
  against its schema by [`SchemaChecker`](Editor/SchemaChecker.cs): an unknown key is an error (the model is closed), and
  so are a value of the wrong type, outside its `enum`, a time span, a uuid or an uri that doesn't parse, and a missing
  required key, looked up along the levels of the section ([`EditorLevels`](Editor/EditorLevels.cs): the party, the
  `Defaults` above it, the central `Defaults:App`; not in a `Defaults` section: the defaults are partial). The names of
  the options of its owner are reserved keys. What only code checks (the model's `Validate`) is the builders'.
  [`EditorDefaults`](Editor/EditorDefaults.cs) gives the values that the absent keys of a modeled section take from a
  `Defaults` level (key by key, as the reader of the models), for the forms. `SetValueAsync` with `createPath` writes a
  key with the missing objects of its path (a section that is `true` becomes an object).
- **Once saved, the Builders check the configuration** ([`SolutionCheckRunner`](Editor/SolutionCheckRunner.cs)): the
  Builder of each of the `"AppContexts"` (`{RootPath}/{Name}.Builder`) is restored, built without running it
  (`-p:CKAppBuilderRunAfterBuild=false`) and run with `--check <file>` (CK.AppBuilder: the run stops before the engine),
  two at a time. A Builder whose CK.AppBuilder package doesn't have the check is neither built nor run (its build would
  run the whole Builder). Their warnings and errors are the problems of the "Build" layer: on the property that starts
  their message when it is found, otherwise on their AppContext; the ones about a deployment are in its view.
- The commands are serialized.
