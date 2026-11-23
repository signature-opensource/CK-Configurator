# CK.Configurator.Catalog

The catalog of what can be configured: for a source (one published profile of a World and its NuGet feeds), what each
package declares that a configuration uses. It is the foundation of the configurator, the application that edits a
Unified Configuration: the catalog tells it which configurable types exist, what they configure and how they relate.

## The source

A `CatalogSource` is a `PublishedProfile` (CK.Packaging.Abstractions: one version of each package produced by a
World) and the `PublishedFeed`s from which its packages are restored: the feeds of the profile, unless others are
given (a profile that predates them has none). The secrets of a feed are never in the source: NuGet finds them where it
always does (the user's NuGet configuration, the environment); the public read credentials (not secrets) are written in
the nuget.config of the worker.

### The World of a source

A `CatalogWorld` is where the profiles are published: a Stack and, for a Long Term Support World, its LTS name.
`CatalogWorld.Parse` reads:

| World | Its `Published` folder |
|---|---|
| `https://github.com/signature-opensource/CK-Stack` (the default World) | `Published/` of the `main` branch |
| `https://github.com/signature-opensource/CK-Stack/tree/main/@net8` (the `CK@net8` LTS World) | `@net8/Published/` of the `main` branch |
| a local folder (the clone of a Stack), or its `@net8` sub folder | `Published/`, `@net8/Published/` |

`CatalogWorld.GetLocation` gives the raw content URL (GitHub) or the file path (local) of a file of that folder.

### Reading a World

`CatalogSources` reads the `Published` folder of a World:

- `CatalogSources.ReadIndexAsync`: its `index.json`, read each time (it changes with every publication);
- `CatalogSources.ReadProfileAsync`: a profile, read once and kept (in memory, and as a file under the cache folder
  given to the constructor for a remote World). A profile never changes, except its deprecation, which is read from the
  index. The file of a profile published before the `TimeVersion` is found by its legacy name
  (`PublishedProfile.GetLegacyProfilePath`);
- `CatalogSources.GetBranchesAsync`: the branches ("stable", "alpha"... "explo/{name}"), each with its latest profile
  (a `CatalogBranch`), the stable line first;
- `CatalogSources.GetProfilesAsync`: the profiles of a branch, its regular and CI builds together, the latest first
  (a `CatalogProfileEntry`: its version, whether it is deprecated, whether it has feeds).

The deprecated profiles are left out unless asked for. A profile without feeds (published before the profiles carried
them, or that can't be read) is listed: a catalog can't be opened on it unless feeds are given.

`CatalogSources.ResolveAsync` obtains the source of a profile of a World, given its version (the branch and the CI build
are in the version).

`CatalogSource.CatalogPackages` are the produced packages that the catalog considers. `CatalogSource.IsCatalogPackage`
excludes the test packages (`CK.Testing.*`, `*.Tests`) and `CK.AppBuilder`.

## The analysis of a package

A `PackageAnalysis` is what a package version declares, nothing more:

| Level | Content |
|---|---|
| `PackageAnalysis` | `PackageId`, `Version`, its `Assemblies` (its runtime assemblies). |
| `AssemblyAnalysis` | `Name`, `Role` (`Basic`, `Model`, `ModelDefiner`, `Engine`), `RequiredEngines`, `Types` (the types of a model that declare something), and what any assembly (an engine included) declares: `PerDeployment` (`[assembly: ConfigurationPerDeployment( "Remote:Address" )]`, a property of the parties of a kind) and `CentralKeys` (`[assembly: CentralConfigurationKey]`: the root keys of the central configuration that its engine reads, with their values per deployment). |
| `TypeAnalysis` | `FullName` (the name by which the ResourceSpace identifies a package), `CSharpName`, `ResourceGroup` (`None`, `Group`, `Package`), `Configurable` (`IConfigurableTypeAttribute`: its kind, name and activation), `Section`, `Locks`, `Secrets`, `PerDeployment`, `PropertyLevels` (the `[Configuration...]` attributes), `Relations` (the topology attributes as declared: `[Requires]`, `[RequiredBy]`, `[Children]`, `[Groups]`, `[Package]`, their `[Optional...]` forms, `IResourceGroupTopologyAttribute`), `OptionOwners` (the owners of an option: `IPartyComponentOption<TOwner>`, a relation that the AppIdentity engine adds), `Facts` (`[PartyClientFact]`), `TypeScriptPackage` (`[TypeScriptPackage]` and its specializations), `Command` (a command interface, abstract or not: its direct bases, its own `[Internal]`/`[Private]`, its `[ExternalName]`). |

What depends on the other packages that are present is not in an analysis: the closures of the relations and the
command families are computed from the analyses of a catalog.

`CatalogJson` writes and reads an analysis (System.Text.Json, source generated; the null values and the empty lists
are not written).

## The cache

A `CatalogCache` keeps the analyses as flat files, one immutable file per package version and per
`PackageAnalysis.Format`: `packages/<package id>/<version>.v<format>.json` (lower case) under its root
(`CatalogCache.DefaultRoot` is `CK-Configurator/Catalog` in the local application data folder). The analysis of a
package version doesn't depend on the source that lists it: the cache is shared by all the sources.

## The worker

`CatalogWorker.AnalyzeAsync` analyzes the packages of a source that are not in the cache. The analysis loads the
types of the packages (the TypeCache of the engine loads the assemblies of its base directory, and the configurator
runs the code of the engines), so it runs in a process whose folder holds exactly the packages of the source: the
**worker**, a generated project in a folder per source.

- `CatalogWorker.WriteProject` writes `CatalogWorker.csproj` (an executable that references the catalog packages of
  the source, at their exact versions, without their build files, and the analyzer), `Program.cs`, a `nuget.config`
  with the feeds of the source and empty `Directory.Build.props`/`.targets` (nothing above the folder is imported).
- The project is built (`dotnet build -c Release`) and run with the `CatalogWorker.Arguments`: the
  `project.assets.json` of the build (it maps a package to its assemblies), the root of the cache and the packages to
  analyze.
- The analyzer is the [CK.Configurator.Catalog.Worker](../CK.Configurator.Catalog.Worker/README.md) package
  (`AnalyzerReference.FromPackage`) or its project (`AnalyzerReference.FromProject`, for the tests).

One source, one worker process: another source is another worker, nothing is unloaded.

### The worker as a server

`CatalogWorker.StartServerAsync` builds the worker of a source (when needed) and starts it as a server
(`CatalogWorker.Arguments.Serve`): the packages are loaded once and the rules run on their real types. The returned
`CatalogWorkerServer` sends the requests one at a time: a `WorkerRequest` is a JSON line on the standard input of the
worker, its `WorkerResponse` a JSON line on its standard output (`WorkerProtocol`); the logs of the worker are its
standard error, relayed to the monitor of the request. Disposing the server closes the input: the worker exits. This
channel is internal to the configurator's backend, it is never exposed.

| Request | Result |
|---|---|
| `ClosureRequest` (`CatalogWorkerServer.GetClosureAsync`) | The packages that a ResourceSpace keeps (`ResPackageClosure.Compute` of CK.ResourceSpace): the `Roots` (full names) are kept, every resource group of the models of the `Packages` (package identifiers) is a candidate. |

A request that logs an error answers its errors (`WorkerResponse.Errors`) and no result.

## The catalog

`Catalog.Load` reads the analyses of the catalog packages of a source from the cache (they must all be there) and
indexes them in memory: `Catalog.FindType` by full name, `Catalog.FindConfigurable` by configuration name (case
insensitive), `Catalog.ConfigurableNames`.

### The commands

`Catalog.Commands` (by name) and `Catalog.FindCommandByInterface` give the `CommandFamily` of the command interfaces,
computed with the rules of the Poco and Cris engines:
- the primary interface of a non abstract command interface is the single primary interface of its non abstract
  command bases, or itself when it has none (two different ones: a warning, the interface is in no family);
- the family is the primary interface and the interfaces whose primary interface it is, from any package;
- its name (`CommandFamily.Name`) is the `[ExternalName]` of the primary interface, its C# name otherwise;
- its exposure (`CommandFamily.Exposure`) is the most restrictive one declared by its interfaces and all their bases,
  the abstract ones included (a command part): `Private`, then `Internal`, then `Public`.
