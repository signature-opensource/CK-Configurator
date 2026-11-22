# CK.Configurator.Catalog.Worker

The analyzer of the catalog: it runs in the worker of a source (see [CK.Configurator.Catalog](../CK.Configurator.Catalog/README.md)),
whose generated `Program.cs` is `return CK.Configurator.Catalog.Worker.WorkerProgram.Run( args );`.

`WorkerProgram.Run` loads every assembly of the worker's base directory with the TypeCache (`AssemblyCache.Run`, the
discover mode on `AppContext.BaseDirectory`), maps each requested package to its runtime assemblies
(`WorkerProgram.ReadPackageAssemblies` reads the `project.assets.json`) and saves the `PackageAnalysis` of each one in
the `CatalogCache`.

The role of an assembly is its `AssemblyKind` (engine, model, model definer, or basic). Only the types of a model are
analyzed: its type set when the assembly cache computed it, its exported types otherwise.

The attributes of CK.ResourceSpace.Abstractions (the configurable types, the `[Configuration...]` attributes, the
topology) are read as known types. The attributes and interfaces of the packages that the analyzer doesn't reference
are read by their full name: `CK.AppIdentity.PartyClientFactAttribute`, `CK.TypeScript.TypeScriptPackageAttribute`
(and its specializations), `CK.Cris.IAbstractCommand`, `CK.Cris.ICommandPart`, `CK.Cris.InternalAttribute` and
`CK.Cris.PrivateAttribute`.
