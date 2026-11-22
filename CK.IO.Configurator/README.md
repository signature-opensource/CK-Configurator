# CK.IO.Configurator

The commands of the configurator, the application that edits the Unified Configuration of a solution. They are handled
by [CK.Configurator](../CK.Configurator/README.md); a client application (the Angular component of the configurator)
sends them.

## The catalog

Every catalog command targets a catalog by its source, the command part `ICommandCatalogSource`: a `Stack` (the URL of
its GitHub repository or the folder of its clone), a `Branch` ("stable", "alpha"... "explo/{name}") and `IsCI` (its CI
builds). The backend keeps the opened catalogs by their source.

| Command | Result |
|---|---|
| `IOpenCatalogCommand` | `ICatalogInfo`: the profile version, the numbers of packages, configurable types and commands. The packages that are not analyzed yet are analyzed first (about a minute when nothing is). |
| `IGetConfigurableTypesQCommand` | `IList<IConfigurableTypeInfo>`: the name, kind, activation, type and package of each configurable type, its section, its values per deployment, its secrets, its restrictions (`ILockInfo`), the capabilities of its properties (`IPropertyLevelInfo`) and the owners of an option. |
| `IGetClosureQCommand` | `IList<string>`: what a selection brings in, the packages that a ResourceSpace keeps from the resource groups of the `Packages` when the `Roots` are required. |
| `IGetCommandsQCommand` | `IList<ICommandInfo>`: the commands, their exposure, their primary interface and the packages of their interfaces. |

The commands have no authentication part: the configurator is a development tool that runs locally. A hosting that
exposes them to a web site adds it, by extending them (a command family can be restricted by an extension).
