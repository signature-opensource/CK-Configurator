# CK.Configurator.Catalog.Worker

The analyzer of the catalog: it runs in the worker of a source (see [CK.Configurator.Catalog](../CK.Configurator.Catalog/README.md)),
whose generated `Program.cs` is `return CK.Configurator.Catalog.Worker.WorkerProgram.Run( args );`.

`WorkerProgram.Run` loads every assembly of the worker's base directory with the TypeCache (`AssemblyCache.Run`, the
discover mode on `AppContext.BaseDirectory`), maps each requested package to its runtime assemblies
(`WorkerProgram.ReadPackageAssemblies` reads the `project.assets.json`) and saves the `PackageAnalysis` of each one in
the `CatalogCache`.

The role of an assembly is its `AssemblyKind` (engine, model, model definer, or basic). Only the types of a model are
analyzed: its type set when the assembly cache computed it, its exported types otherwise. The assembly attributes are
read on every assembly: an engine declares the structure it reads.

The attributes of CK.ResourceSpace.Abstractions (the configurable types, the `[Configuration...]` attributes, the
topology) are read as known types. The attributes and interfaces of the packages that the analyzer doesn't reference
are read by their full name: `CK.Core.CentralConfigurationKeyAttribute` (an older profile doesn't have it), `CK.AppIdentity.IPartyComponentOption<TOwner>`, `CK.AppIdentity.PartyClientFactAttribute`, `CK.TypeScript.TypeScriptPackageAttribute`
(and its specializations), `CK.Cris.IAbstractCommand`, `CK.Cris.ICommandPart`, `CK.Cris.InternalAttribute` and
`CK.Cris.PrivateAttribute`.

The source file of a type and of its configuration model comes from the PDB of its assembly (embedded, or the `.pdb`
file next to it): `SourceLocator` reads the documents of the type (those of its methods, the `TypeDefinitionDocuments`
of a type without method bodies; the one named after the type for a partial type) and maps them with the SourceLink
of the PDB. The analysis keeps this raw URL; `SourceFileUrl` (CK.Configurator.Catalog) derives the page of the file on
GitHub, GitLab or Gitea, at the tag of the package version (`v{Version}`) or at the commit.

The packages of the analyzer (`CatalogWorker.IsAnalyzerPackage`: CK.Configurator.Catalog and its worker) are never
referenced at the version of the profile: the worker runs its own code, and analyzes its own assemblies for them.

With `--serve` (`CatalogWorker.Arguments.Serve`), `WorkerProgram.Run` loads the assemblies once and answers the
`WorkerRequest` lines of its standard input (its logs go to its standard error) until the input is closed. A
`ClosureRequest` runs `ResPackageClosure.Compute` (CK.ResourceSpace) on the concrete resource groups of the models of
the requested packages (an abstraction is never a candidate).
