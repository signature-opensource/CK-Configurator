# CK.Ng.Configurator

The Angular side of the configurator: the TypeScript package `NgConfiguratorPackage` (it requires the Cris endpoint of
CK.Ng.Cris.AspNet, the ng-zorro components of CK.Ng.Zorro and the Markdown component of CK.Ng.Markdown, and registers the commands of
[CK.IO.Configurator](../CK.IO.Configurator/README.md)): the opened catalog of the application, its picker and the
catalog page.

## The opened catalog: `CatalogSource` and its picker

`CatalogSource` (`catalog-source.ts`, a root service) is the opened catalog of the application and the choice of its
source. The pages of the configurator read it: switching between them reloads nothing.

The picker `<ck-catalog-source-picker>` (`CatalogSourcePickerComponent`) chooses a profile of a World, on one line:

- the World (the URL of the GitHub repository of a Stack, followed by `/tree/main/@{LTS}` for an LTS World, or a local
  folder): "Load" lists the Worlds of its Stack (`IGetWorldsQCommand`: a World list appears when the Stack has LTS
  Worlds) and the branches of the World (`IGetBranchesQCommand`), each with the date of its latest profile;
- the branch: its profiles are listed (`IGetProfilesQCommand`), the latest first, the CI builds tagged "CI";
- the profile: the latest one that has feeds is selected, the ones without feeds are disabled. The deprecated profiles
  are shown only when asked for.

"Open" (`CatalogSource.openAsync`) sends `IOpenCatalogCommand` and reads the configurable types, the commands and the
packages of the catalog (`info`, `types`, `commands`, `packages`). `CatalogSource.source( command )` sets the opened
source on a command.

The shell of an application shows the picker in its header and sets `CatalogSource.pickerInShell`; otherwise the
catalog page shows it.

## The catalog page

`CatalogPageComponent` is routed at `ckomposable/catalog` (the `ckomposable/` prefix leaves the other routes to the
application: a web application can host the page as it is). It shows the opened catalog:

- **the configurable types**, by level (the Builder aspects, the App aspects, the party features, the client features),
  each option under its owner (`IConfigurableTypeInfo.OptionOwners`) in the level of its owner, with a detail panel
  (section, values per deployment, secrets, restrictions, levels of the properties);
- **what a selection brings in:** the checked types are the roots of `IGetClosureQCommand` (the candidates are the
  packages of the configurable types);
- **the commands**, with their summary, their exposure and their packages;
- **the packages**, with their description (a tooltip), their direct dependencies and what they contain (links to the
  other tabs); an opened package shows its readme.

The detail panel shows the summary and the remarks of a type, and a package name opens its readme
(`IGetPackageQCommand`). The Markdown is rendered by the `<ck-markdown>` component of CK.Ng.Markdown (CK-TS-Angular).

The page is read-only: its own state (the selection, the closure, the opened packages) is reset when another catalog
is opened. The texts are in the `Res/ts-locales` of the page, of the picker and of the package (the names of the
pages: "CK.Configurator.CatalogPage", "CK.Configurator.EditorPage"), default and French.

The backend of these commands is [CK.Configurator](../CK.Configurator/README.md): an application that references both
serves the page.
