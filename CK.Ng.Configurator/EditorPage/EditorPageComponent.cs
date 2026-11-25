using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The editor page, at "ckomposable/editor": the central configuration ("ckonf.json") of a solution and its deployments,
/// held by the backend (CK.Configurator). A view (the central configuration or the merged configuration of a deployment)
/// shows the tree of its properties (typed by the catalog of the solution: its "$configurator"; their origin in a
/// deployment) with their problems, the selected property (its value, its documentation, its comment), the palette of
/// what can be added to it and the problems of the view. Every edit is a command (undo/redo); a save keeps the comments
/// of the files, then the builders of the solution check the configuration.
/// </summary>
[NgRoutedComponent<AppComponent>( Route = "ckomposable/editor" )]
[Package<NgConfiguratorPackage>]
public sealed class EditorPageComponent : NgRoutedComponent
{
}
