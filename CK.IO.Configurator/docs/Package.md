# CK.IO.Configurator

The commands of the configurator: the catalog of what can be configured in a published profile of a World, and the
queries that list the branches and the profiles of a World.

The catalog commands (`IOpenCatalogCommand`, `IGetConfigurableTypesQCommand`, `IGetClosureQCommand`,
`IGetCommandsQCommand`) are targeted by their source (`ICommandCatalogSource`); `IGetBranchesQCommand` and
`IGetProfilesQCommand` list the branches and the profiles. The editor commands (`IOpenSolutionCommand`, `ISetValueCommand`,
`IAddPropertyCommand`, `IRemovePropertyCommand`, `IUndoCommand`, `IRedoCommand`, `ISaveCommand`...) edit the central
`ckonf.json` of a solution and return its `IEditorState`.

See the [README](https://github.com/signature-opensource/CK-Configurator/blob/stable/CK.IO.Configurator/README.md).
