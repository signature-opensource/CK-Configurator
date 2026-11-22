# CK.Configurator.Catalog

The catalog of what can be configured: for a source (one published profile of a World and its NuGet feeds), what each
package declares that a configuration uses. It is the foundation of the configurator, the application that edits a
Unified Configuration: the catalog tells it which configurable types exist, what they configure and how they relate.

## The source

A `CatalogSource` is a `PublishedProfile` (CK.Packaging.Abstractions: one version of each package produced by a
World) and its feeds (`CatalogFeed`: a name and a URL or a local folder). The credentials of a feed are never in the
source: NuGet finds them where it always does (the user's NuGet configuration, the environment).

`CatalogSource.CatalogPackages` are the produced packages that the catalog considers. `CatalogSource.IsCatalogPackage`
excludes the test packages (`CK.Testing.*`, `*.Tests`) and `CK.AppBuilder`.

## The analysis of a package

A `PackageAnalysis` is what a package version declares, nothing more:

| Level | Content |
|---|---|
| `PackageAnalysis` | `PackageId`, `Version`, its `Assemblies` (its runtime assemblies). |
| `AssemblyAnalysis` | `Name`, `Role` (`Basic`, `Model`, `ModelDefiner`, `Engine`), `RequiredEngines`, `Types` (the types of a model that declare something). |
| `TypeAnalysis` | `FullName` (the name by which the ResourceSpace identifies a package), `CSharpName`, `ResourceGroup` (`None`, `Group`, `Package`), `Configurable` (`IConfigurableTypeAttribute`: its kind, name and activation), `Section`, `Locks`, `Secrets`, `PerDeployment`, `PropertyLevels` (the `[Configuration...]` attributes), `Relations` (the topology attributes as declared: `[Requires]`, `[RequiredBy]`, `[Children]`, `[Groups]`, `[Package]`, their `[Optional...]` forms, `IResourceGroupTopologyAttribute`), `Facts` (`[PartyClientFact]`), `TypeScriptPackage` (`[TypeScriptPackage]` and its specializations), `Command` (a command interface or a command part: its direct bases and its own `[Internal]`/`[Private]`). |

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

## The catalog

`Catalog.Load` reads the analyses of the catalog packages of a source from the cache (they must all be there) and
indexes them in memory: `Catalog.FindType` by full name, `Catalog.FindConfigurable` by configuration name (case
insensitive), `Catalog.ConfigurableNames`.
