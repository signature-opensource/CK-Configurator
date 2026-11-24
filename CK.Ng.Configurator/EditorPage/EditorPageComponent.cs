using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The editor page, at "ckomposable/editor": the central configuration ("ckonf.json") of a solution, held by the backend
/// (CK.Configurator). The tree of its properties (typed by the catalog of the document: its "$configurator") with their
/// problems, the selected property (its value, its documentation, its comment), the palette of what can be added to it
/// and the problems of the document. Every edit is a command (undo/redo); a save keeps the comments of the file.
/// </summary>
[NgRoutedComponent<AppComponent>( Route = "ckomposable/editor" )]
[Package<NgConfiguratorPackage>]
public sealed class EditorPageComponent : NgRoutedComponent
{
}
