# CK.Ng.Configurator

The Angular side of the configurator: the TypeScript package `NgConfiguratorPackage` (it requires the Cris endpoint of
CK.Ng.Cris.AspNet and the ng-zorro components of CK.Ng.Zorro, and registers the commands of
[CK.IO.Configurator](../CK.IO.Configurator/README.md)) and its catalog page.

## The catalog page

`CatalogPageComponent` is routed at the root of the client application (`Route = ""`). Its source form names a Stack
(the URL of its GitHub repository or the folder of its clone), a branch and its CI builds; "Open" sends
`IOpenCatalogCommand`, then lists:

- **the configurable types**, each option under its owner (`IConfigurableTypeInfo.OptionOwners`), with a detail panel
  (section, values per deployment, secrets, restrictions, levels of the properties);
- **what a selection brings in:** the checked types are the roots of `IGetClosureQCommand` (the candidates are the
  packages of the configurable types);
- **the commands**, with their exposure and their packages.

The page is read-only. Its texts are in `CatalogPage/Res/ts-locales` (default and French).

The backend of these commands is [CK.Configurator](../CK.Configurator/README.md): an application that references both
serves the page.
