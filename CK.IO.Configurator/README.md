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

## The editor

The editor edits the central configuration (`ckonf.json`) of one solution and the overlays of its deployments
(`Deployment/#Production/ckonf.json`), held by the backend. A view is the central configuration or a deployment (the
`ICommandEditorView` part of a command: its `Deployment`, null for the central one): the view of a deployment is its
merged configuration, and its edits are written in its overlay. Every command returns the `IEditorState` of a view: its
folder and file, whether a document is dirty, can undo or redo, changed on disk, the source of the catalog (the
`"$configurator"` root key of the central configuration: `CatalogWorld`, `CatalogVersion`), the documents
(`IEditorDocument`: name, base, file, dirty, new, changed on disk, the counts of the errors and warnings of their view),
the properties of the view (`IEditorNode`, in document order: path, name, depth, kind, value, comment, the `Role` and
the `TypeName` that the catalog gives them, their `Origin` in the view of a deployment, their lock: `LockLevel`,
`LockSource`, `CanUnlock`, the capability of the code: `MaxLevel`, and their secret: `SecretName`, `SecretStatus` in the
central view), its problems
(`IEditorProblem`: the "Static" checks once the catalog is opened, a modeled section checked against its schema included, the "Build" ones of the check by the builders, and
the `Fix` that the editor offers: "WriteHere" for an inherited value per deployment, its value written by an
`ISetValueCommand`) and
the status of that check (`CheckStatus`, `CheckIsStale`), and the values that the absent keys of the modeled sections take
from a `Defaults` level (`IEditorDefaultedValue`: path, value, the property that gives it).

| Command | What it does |
|---|---|
| `IOpenSolutionCommand` (`Folder`) | Opens the `ckonf.json` of the folder and the overlays of its deployments (an opened solution is closed). |
| `IGetEditorStateQCommand` | Returns the state of a view. |
| `ISetValueCommand` (`Path`, `Json`, `CreatePath`) | Sets the value of a property; the comments around it are kept. In a deployment, it is written in its overlay. `CreatePath` adds a missing property with the objects of its path (the forms). |
| `IAddPropertyCommand` (`ObjectPath`, `Name`, `Json`) | Adds a property at the end of an object, with the indentation of its siblings (enabling an option or a feature). |
| `IRemovePropertyCommand` (`Path`) | Removes a property with its comma and its comments; in a deployment, an inherited one is removed by a `null`. |
| `IRevertPropertyCommand` (`Path`) | In a deployment, removes a property from its overlay: it is inherited again. |
| `ILockPropertyCommand` (`Path`, `Level`) | In the central configuration, locks a property: an entry of the `"$LOCK"` of its App or AppContext aspect. |
| `IUnlockPropertyCommand` (`Path`) | Removes the literal pattern of a `"$LOCK"` that names the property. |
| `IRemoveLockEntryCommand` (`ObjectPath`, `Pattern`) | Removes an entry of the `"$LOCK"` of an object, even one that names no property. |
| `IMakeSecretCommand` (`Path`, `Name`, `IsOptional`, `Overwrite`) | Makes a value a secret: its handle in the configuration; in the central one, the value moves to `#Dev/Secrets.json`. |
| `ICreateDeploymentCommand` (`Name`, `Base`, `WriteValuesPerDeployment`) | Creates a deployment ("#Staging", "Paris/Shop/#Production"), written at the next save, optionally with the values per deployment that it would inherit. |
| `ISetCatalogSourceCommand` (`World`, `Version`) | Writes the `"$configurator"` root key: the catalog that types and checks the documents once it is opened (`IOpenCatalogCommand`). |
| `IUndoCommand`, `IRedoCommand` | Undo and redo the edits, in any document: the state is the view of the changed one. |
| `ISaveCommand` (`Overwrite`) | Saves the dirty documents (a file that changed on disk is overwritten only with `Overwrite`), then the builders check the configuration. |
| `ICheckSolutionCommand` | Checks the saved configuration with the builders again. |
| `IReloadCommand` | Reads the documents again (the edits are lost). |
| `ICloseSolutionCommand` | Closes the solution. |

A path is the property names separated by `:`, case insensitive (`"AppContexts:Front:App:Local:HttpEndpoint"`); a
value is JSON text. An edit that fails (a missing path, an existing property, an invalid value) is an error result.

The commands have no authentication part: the configurator is a development tool that runs locally. A hosting that
exposes them to a web site adds it, by extending them (a command family can be restricted by an extension).
