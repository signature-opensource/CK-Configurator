# CK.Configurator

The backend of the configurator: the handlers of the CK.IO.Configurator commands (`ConfiguratorCatalogHandlers`) and the
opened catalogs with their workers (`ConfiguratorCatalogService`), the document of the editor (`ConfiguratorEditorService`,
the central `ckonf.json` of a solution and its deployments edited with their comments kept, undo/redo, the detection of
a change on disk, the static checks and the check by the builders of the solution) and
its handlers (`ConfiguratorEditorHandlers`).

See the [README](https://github.com/signature-opensource/CK-Configurator/blob/stable/CK.Configurator/README.md).
