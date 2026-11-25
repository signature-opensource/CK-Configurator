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
is opened. A type that has a configuration model shows its keys as a table (`schemaKeys` of `config-schema.ts`: their
path, type, default, flags and documentation) and its raw JSON Schema. The texts are in the `Res/ts-locales` of the
page, of the picker, of the form and of the package (the names of the
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
  and its "new" tag; "New deployment" creates one (its name, the deployment it overlays and "Write its values per
  deployment", checked by default). In the view of a deployment, what it inherits is dimmed (the name of the base it
  comes from), what its overlay writes is marked and what it removes is struck through; the selected property says its
  origin and offers "Write it here" (inherited) or "Revert to inherited" (written), and "Remove" is "Remove here". An
  inherited value per deployment has "Write it here" in the problems too. An undo shows the view of the document it changed.
- **The contextual menu** of a row of the tree (a right click, or Shift+F10 and the menu key on the selected row): the
  actions that apply to it: Collapse/Expand, Add a property, Write it here, Revert to inherited, Make it a secret...,
  Lock (the levels up to the capability of the code) and Unlock, Remove (or Remove here), Copy the path. "Make it a
  secret..." proposes the last two names of the path as the name of the secret, "Optional", and asks before replacing
  a development secret of the same name.
- **The tree** (left): the properties in document order, the objects collapsible, the typed ones (a builder, an aspect,
  a party, a feature, a client, an option) with their role, the counts of their errors and warnings, a lock mark (its
  level and what locks it in a tooltip) and a key mark on a secret. The `"$LOCK"` are not rows: the object that holds
  one (an App, a Builder aspect) has a lock counter (its patterns and levels in a tooltip) and lists its entries
  (pattern, level, "Remove") in the detail pane. An absent value that a literal pattern locks is an "(absent)" row in
  italic at its place (its detail says what its lock means), with Unlock. A builder is shown
  without its ".Builder" suffix (in the paths too); the keys of the document don't change. The three panes take the
  height of the window and scroll on their own.
- **The selected property** (centre): its path, its role and its configurable type, its problems, the comment written
  above it, its value (a switch for a boolean, a text for a string, JSON text otherwise; "Edit as JSON" replaces it by
  any value), the addition of a property to an object, its removal, and the documentation of its type (its summary
  and remarks, in Markdown).
- **The form of a modeled section** (`ck-model-form`, `ModelFormComponent`: the selected section's type has a
  configuration model, its JSON Schema in the catalog): every property of the model, present or not, its editor by
  kind (a switch, a number, a select for an `enum`, tags for a list of strings, a text for a string, a time span, a uuid
  or an uri, a fieldset for a nested model, one fieldset per entry of a dictionary and "Add an entry", the JSON editor
  for another list and an opaque section with a link to its schema), "required", its level, secret and per deployment
  flags, its lock and its origin, its documentation, and for an absent key the value that it takes from a `Defaults`
  level or the default of the model. An edit writes the key (`ISetValueCommand` with `CreatePath`); "Reset to default"
  removes it ("Revert to inherited" in a deployment); a secret property offers "Make it a secret". The form is driven by
  `ModelSchema` (`config-schema.ts`, shared with the catalog page's table of the keys, `schemaKeys`).
- **The palette** (right): what the catalog offers at the selected property, added as `true`: the party features of a
  party, the client features of a client, the options of a feature or of a client feature, the aspects of an
  `"Aspects"`. Then **the problems** of the document: a click selects the property.
- **Undo, Redo, Save, Close**, the "Not saved" tag; a file that changed on disk offers "Reload" or "Overwrite".
- **The check by the builders:** its status after a save (running, checked, problems found, "changed since") and
  "Check"; the state is polled while it runs, and its problems are tagged "build".

The backend of these commands is [CK.Configurator](../CK.Configurator/README.md): an application that references both
serves the pages.
