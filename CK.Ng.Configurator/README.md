# CK.Ng.Configurator

The Angular side of the configurator: the TypeScript package `NgConfiguratorPackage` (it requires the Cris endpoint of
CK.Ng.Cris.AspNet, the ng-zorro components of CK.Ng.Zorro and the Markdown component of CK.Ng.Markdown, and registers the commands of
[CK.IO.Configurator](../CK.IO.Configurator/README.md) and the folder dialog of CK.IO.AppHost.Desktop): the opened
catalog of the application, its picker, the catalog page and the editor page.

## The opened catalog: `CatalogSource` and its picker

`CatalogSource` (`catalog-source.ts`, a root service) is the opened catalog of the application and the choice of its
source. The pages of the configurator read it: switching between them reloads nothing.

The picker `<ck-catalog-source-picker>` (`CatalogSourcePickerComponent`) chooses a profile of a World, one field per row (it fits a dialog or a narrow window):

- the World (the URL of the GitHub repository of a Stack, followed by `/tree/main/@{LTS}` for an LTS World, or a local
  folder): "Load" lists the Worlds of its Stack (`IGetWorldsQCommand`: a World list appears when the Stack has LTS
  Worlds) and the branches of the World (`IGetBranchesQCommand`), each with the date of its latest profile;
- the branch: its profiles are listed (`IGetProfilesQCommand`), the latest first, the CI builds tagged "CI";
- the profile: the latest one that has feeds is selected, the ones without feeds are disabled. The deprecated profiles
  are shown only when asked for.

"Open" (`CatalogSource.openAsync`) sends `IOpenCatalogCommand` and reads the configurable types, the commands and the
packages of the catalog (`info`, `types`, `commands`, `packages`). `CatalogSource.source( command )` sets the opened
source on a command.

`<ck-catalog-source-selector>` (`CatalogSourceSelectorComponent`) shows the opened catalog on one line,
`[Stack url - World - profile]` (or `<no catalog>`), with an "Edit" button that opens the picker in a dialog (closed once
a catalog is opened). The shell of an application shows it in its header and sets `CatalogSource.pickerInShell`;
otherwise the pages show the picker.

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

## The editor page

`EditorPageComponent` is routed at `ckomposable/editor`. It edits the central configuration (`ckonf.json`) of a
solution and its deployments, held by the backend: every action is an editor command that returns the whole state of
a view (`IEditorState`, see [CK.IO.Configurator](../CK.IO.Configurator/README.md#the-editor)).

- **Opening:** the folder of the solution is typed (the last one is remembered in the browser) or chosen with the
  folder dialog of a desktop window ("Browse...", `IPickFolderCommand`: hidden when the command fails, in a browser).
- **The catalog of the document** (its `"$configurator"`) is opened with `CatalogSource.openSourceAsync`: it types and
  checks the document. Without one, the opened catalog of the picker can be used for the document
  (`ISetCatalogSourceCommand`: written in the document); an opened catalog that is not the one of the document is a
  warning.
- **The view:** the central configuration ("Development") or a deployment, each with its counts of errors and warnings
  and its "new" tag; "New deployment" creates one (its name and the deployment it overlays). In the view of a
  deployment, what it inherits is dimmed (the name of the base it comes from), what its overlay writes is marked and
  what it removes is struck through; the selected property says its origin and offers "Revert to inherited", and
  "Remove" is "Remove here". An undo shows the view of the document it changed.
- **The tree** (left): the properties in document order, the objects collapsible, the typed ones (a builder, an aspect,
  a party, a feature, a client, an option) with their role, the counts of their errors and warnings. A builder is shown
  without its ".Builder" suffix (in the paths too); the keys of the document don't change. The three panes take the
  height of the window and scroll on their own.
- **The selected property** (centre): its path, its role and its configurable type, its problems, the comment written
  above it, its value (a switch for a boolean, a text for a string, JSON text otherwise; "Edit as JSON" replaces it by
  any value), the addition of a property to an object, its removal, and the documentation of its type (its summary
  and remarks, in Markdown).
- **The palette** (right): what the catalog offers at the selected property, added as `true`: the party features of a
  party, the client features of a client, the options of a feature or of a client feature, the aspects of an
  `"Aspects"`. Then **the problems** of the document: a click selects the property.
- **Undo, Redo, Save, Close**, the "Not saved" tag; a file that changed on disk offers "Reload" or "Overwrite".
- **The check by the builders:** its status after a save (running, checked, problems found, "changed since") and
  "Check"; the state is polled while it runs, and its problems are tagged "build".

The backend of these commands is [CK.Configurator](../CK.Configurator/README.md): an application that references both
serves the pages.
