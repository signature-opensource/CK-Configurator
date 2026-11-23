# CK.Ng.Configurator

The Angular side of the configurator: the TypeScript package `NgConfiguratorPackage` (it requires the Cris endpoint of
CK.Ng.Cris.AspNet and the ng-zorro components of CK.Ng.Zorro, and registers the commands of
[CK.IO.Configurator](../CK.IO.Configurator/README.md)) and its catalog page.

## The catalog page

`CatalogPageComponent` is routed at the root of the client application (`Route = ""`). Its source form, on one line,
chooses a profile of a World:

- the World (the URL of the GitHub repository of a Stack, followed by `/tree/main/@{LTS}` for an LTS World, or a local
  folder): "Load" lists its branches (`IGetBranchesQCommand`), each with the date of its latest profile;
- the branch: its profiles are listed (`IGetProfilesQCommand`), the latest first, the CI builds tagged "CI";
- the profile: the latest one that has feeds is selected, the ones without feeds are disabled. The deprecated profiles
  are shown only when asked for.

"Open" sends `IOpenCatalogCommand`, then lists:

- **the configurable types**, by level (the Builder aspects, the App aspects, the party features, the client features),
  each option under its owner (`IConfigurableTypeInfo.OptionOwners`) in the level of its owner, with a detail panel
  (section, values per deployment, secrets, restrictions, levels of the properties);
- **what a selection brings in:** the checked types are the roots of `IGetClosureQCommand` (the candidates are the
  packages of the configurable types);
- **the commands**, with their exposure and their packages.

The page is read-only. Its texts are in `CatalogPage/Res/ts-locales` (default and French).

The backend of these commands is [CK.Configurator](../CK.Configurator/README.md): an application that references both
serves the page.
