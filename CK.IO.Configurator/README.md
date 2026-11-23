# CK.IO.Configurator

The commands of the configurator, the application that edits the Unified Configuration of a solution. They are handled
by [CK.Configurator](../CK.Configurator/README.md); a client application (the Angular component of the configurator)
sends them.

## The catalog

Every catalog command targets a catalog by its source, the command part `ICommandCatalogSource`: a `World` (the command
part `ICommandCatalogWorld`) and the `Version` of one of its published profiles (the version names the branch and the CI
build). The World is the URL of the GitHub repository of a Stack (its default World), followed by `/tree/main/@net8`
for its `@net8` LTS World, or the local folder of the clone of the Stack (its `@net8` sub folder for the LTS World). The
backend keeps the opened catalogs by their source.

Two queries list what a World published, to choose a source:

| Query | Result |
|---|---|
| `IGetWorldsQCommand` (`World`) | `IList<string>`: the Worlds of the Stack of the World, its default World first, then its LTS Worlds (the ones that the index of the default World names). |
| `IGetBranchesQCommand` (`World`, `IncludeDeprecated`) | `IList<IBranchInfo>`: the branches ("stable", "alpha"... "explo/{name}"), each with its latest profile, the stable line first. |
| `IGetProfilesQCommand` (`World`, `Branch`, `IncludeDeprecated`) | `IList<IProfileInfo>`: the profiles of a branch, its regular and CI builds together, the latest first. |

An `IProfileInfo` is a `Version`, its `Date` and `ClashNumber`, `IsCI`, `IsDeprecated` and `HasFeeds` (a profile
published before the profiles carried their feeds has none: a catalog can't be opened on it).

| Command | Result |
|---|---|
| `IOpenCatalogCommand` | `ICatalogInfo`: the numbers of packages, configurable types and commands. The packages that are not analyzed yet are analyzed first (about a minute when nothing is). |
| `IGetConfigurableTypesQCommand` | `IList<IConfigurableTypeInfo>`: the name, kind, activation, type and package of each configurable type, its section, its values per deployment, its secrets, its restrictions (`ILockInfo`), the capabilities of its properties (`IPropertyLevelInfo`) and the owners of an option. |
| `IGetClosureQCommand` | `IList<string>`: what a selection brings in, the packages that a ResourceSpace keeps from the resource groups of the `Packages` when the `Roots` are required. |
| `IGetCommandsQCommand` | `IList<ICommandInfo>`: the commands, their exposure, their primary interface and the packages of their interfaces. |
| `IGetPackageQCommand` (`PackageId`) | `IPackageInfo`: its identifier, its version and its readme (Markdown). |

An `IConfigurableTypeInfo` and an `ICommandInfo` (its primary interface) carry the `Summary` and the `Remarks` of the
Xml documentation of their type, in Markdown.

The commands have no authentication part: the configurator is a development tool that runs locally. A hosting that
exposes them to a web site adds it, by extending them (a command family can be restricted by an extension).
