using CK.Core;
using CK.Cris;
using System.Collections.Generic;

namespace CK.IO.Configurator;

/// <summary>
/// The view of the solution that a command of the editor targets: the central configuration (null) or a deployment (its
/// name, "#Production"). The view of a deployment is its merged configuration, and its edits are written in its overlay
/// ("Deployment/#Production/ckonf.json"). The returned state is the one of this view.
/// </summary>
public interface ICommandEditorView : ICommandPart
{
    /// <summary>
    /// Gets or sets the deployment of the view (its name: "#Production", "Paris/Shop/#Staging"), null for the central
    /// configuration.
    /// </summary>
    string? Deployment { get; set; }
}

/// <summary>
/// Opens a solution: its central configuration ("ckonf.json") and its deployments (the overlays of its "Deployment"
/// folder), the documents of the editor. The backend holds one solution: an opened one (and its unsaved edits) is closed.
/// </summary>
public interface IOpenSolutionCommand : ICommand<IEditorState>
{
    /// <summary>
    /// Gets or sets the folder of the solution: its "ckonf.json" is opened.
    /// </summary>
    string Folder { get; set; }
}

/// <summary>
/// Gets the state of a view (a closed state when no solution is opened).
/// </summary>
public interface IGetEditorStateQCommand : ICommand<IEditorState>, ICommandEditorView
{
}

/// <summary>
/// Closes the opened solution (its unsaved edits are lost).
/// </summary>
public interface ICloseSolutionCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Sets the value of a property. The comments around it are kept. In the view of a deployment, the value is written in
/// its overlay (with the objects of its path that the overlay doesn't have yet).
/// </summary>
public interface ISetValueCommand : ICommand<IEditorState>, ICommandEditorView
{
    /// <summary>
    /// Gets or sets the path of the property: its names separated by ':' ("Builders:App/App.Builder:App:Local:HttpEndpoint").
    /// The names are case insensitive, like the configuration keys.
    /// </summary>
    string Path { get; set; }

    /// <summary>
    /// Gets or sets the new value (JSON text: <c>"text"</c>, <c>12</c>, <c>true</c>, <c>{ ... }</c>...).
    /// </summary>
    string Json { get; set; }
}

/// <summary>
/// Adds a property at the end of an object, with the indentation of its siblings (an error if it exists). Enabling an
/// option or a feature is adding its key (<c>true</c> or a section). In the view of a deployment, it is written in its
/// overlay.
/// </summary>
public interface IAddPropertyCommand : ICommand<IEditorState>, ICommandEditorView
{
    /// <summary>
    /// Gets or sets the path of the object (empty for the root object).
    /// </summary>
    string ObjectPath { get; set; }

    /// <summary>
    /// Gets or sets the name of the property.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the value (JSON text).
    /// </summary>
    string Json { get; set; }
}

/// <summary>
/// Removes a property with its comma, the comments above it and the comment that follows it on its line. In the view of
/// a deployment, an inherited property is removed by a <c>null</c> in the overlay ("Remove here"); a property that only
/// the overlay writes is removed from it.
/// </summary>
public interface IRemovePropertyCommand : ICommand<IEditorState>, ICommandEditorView
{
    /// <summary>
    /// Gets or sets the path of the property.
    /// </summary>
    string Path { get; set; }
}

/// <summary>
/// In the view of a deployment, removes a property from its overlay: the deployment inherits it again ("Revert to
/// inherited"). The objects of its path that become empty are removed too when they have no comment and the deployment
/// inherits an object there (an empty object would otherwise create it).
/// </summary>
public interface IRevertPropertyCommand : ICommand<IEditorState>, ICommandEditorView
{
    /// <summary>
    /// Gets or sets the path of the property.
    /// </summary>
    string Path { get; set; }
}

/// <summary>
/// Creates a deployment: its overlay ("Deployment/{Name}/ckonf.json") is written at the next save. The returned state is
/// the view of the new deployment.
/// </summary>
public interface ICreateDeploymentCommand : ICommand<IEditorState>
{
    /// <summary>
    /// Gets or sets the name of the deployment: its path in the "Deployment" folder, its environment ('#' and a name) after
    /// the parts of its domain ("#Staging", "Paris/Shop/#Production").
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the deployment that the new one overlays (its <c>"$BASE"</c>), null for the central configuration.
    /// </summary>
    string? Base { get; set; }
}

/// <summary>
/// Sets the source of the catalog of the solution: the <c>"$configurator"</c> root key of its central configuration (an
/// edit). The catalog types and checks the documents once it is opened (<see cref="IOpenCatalogCommand"/>).
/// </summary>
public interface ISetCatalogSourceCommand : ICommand<IEditorState>, ICommandCatalogSource, ICommandEditorView
{
}

/// <summary>
/// Undoes the last edit, in any document: the returned state is the view of the document that it changed.
/// </summary>
public interface IUndoCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Redoes the last undone edit: the returned state is the view of the document that it changed.
/// </summary>
public interface IRedoCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Saves the documents that have unsaved edits. When one of them changed on disk since it was read
/// (<see cref="IEditorDocument.ChangedOnDisk"/>), the save is refused unless <see cref="Overwrite"/> is true. Once
/// saved, the builders of the solution check the configuration (<see cref="IEditorState.CheckStatus"/>).
/// </summary>
public interface ISaveCommand : ICommand<IEditorState>, ICommandEditorView
{
    /// <summary>
    /// Gets or sets whether the files that changed on disk are overwritten.
    /// </summary>
    bool Overwrite { get; set; }
}

/// <summary>
/// Reads the documents again (and finds the deployments again): the edits (and their undo history) are lost.
/// </summary>
public interface IReloadCommand : ICommand<IEditorState>, ICommandEditorView
{
}

/// <summary>
/// Checks the saved configuration again with the builders of the solution (see <see cref="IEditorState.CheckStatus"/>).
/// </summary>
public interface ICheckSolutionCommand : ICommand<IEditorState>, ICommandEditorView
{
}

/// <summary>
/// The state of a view of the solution, returned by every command of the editor.
/// </summary>
public interface IEditorState : IPoco
{
    /// <summary>
    /// Gets or sets the folder of the opened solution (null when no solution is opened).
    /// </summary>
    string? Folder { get; set; }

    /// <summary>
    /// Gets or sets the deployment of this view (null for the central configuration).
    /// </summary>
    string? View { get; set; }

    /// <summary>
    /// Gets or sets the path of the file of this view (the central "ckonf.json" or the overlay of the deployment).
    /// </summary>
    string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets whether a document of the solution has unsaved edits.
    /// </summary>
    bool IsDirty { get; set; }

    /// <summary>
    /// Gets or sets whether an edit can be undone.
    /// </summary>
    bool CanUndo { get; set; }

    /// <summary>
    /// Gets or sets whether an undone edit can be redone.
    /// </summary>
    bool CanRedo { get; set; }

    /// <summary>
    /// Gets or sets whether a document changed on disk (or disappeared) since it was read or saved: a save must
    /// overwrite it, or the solution is reloaded.
    /// </summary>
    bool ChangedOnDisk { get; set; }

    /// <summary>
    /// Gets or sets the World of the catalog of the solution (the <c>"$configurator"</c> root key of its central
    /// configuration), or null.
    /// </summary>
    string? CatalogWorld { get; set; }

    /// <summary>
    /// Gets or sets the version of the catalog of the solution (the <c>"$configurator"</c> root key of its central
    /// configuration), or null.
    /// </summary>
    string? CatalogVersion { get; set; }

    /// <summary>
    /// Gets or sets the check of the saved configuration by the builders of the solution: "None" (not run since the
    /// solution was opened), "Running", "Succeeded" or "Failed". Its problems are the ones of <see cref="IEditorProblem.Layer"/> "Build".
    /// </summary>
    string CheckStatus { get; set; }

    /// <summary>
    /// Gets or sets whether the documents changed since the check: its problems may be outdated.
    /// </summary>
    bool CheckIsStale { get; set; }

    /// <summary>
    /// Gets the documents of the solution: the central configuration first (its <see cref="IEditorDocument.Name"/> is
    /// null), then the deployments sorted by name.
    /// </summary>
    IList<IEditorDocument> Documents { get; }

    /// <summary>
    /// Gets the properties of the view, in document order (a property is followed by the ones of its value).
    /// </summary>
    IList<IEditorNode> Nodes { get; }

    /// <summary>
    /// Gets the problems of the view.
    /// </summary>
    IList<IEditorProblem> Problems { get; }
}

/// <summary>
/// A document of the solution: the central configuration or the overlay of a deployment.
/// </summary>
public interface IEditorDocument : IPoco
{
    /// <summary>
    /// Gets or sets the name of the deployment, null for the central configuration.
    /// </summary>
    string? Name { get; set; }

    /// <summary>
    /// Gets or sets the deployment that this one overlays (its <c>"$BASE"</c>), or null.
    /// </summary>
    string? Base { get; set; }

    /// <summary>
    /// Gets or sets the path of the file.
    /// </summary>
    string FilePath { get; set; }

    /// <summary>
    /// Gets or sets whether the document has unsaved edits.
    /// </summary>
    bool IsDirty { get; set; }

    /// <summary>
    /// Gets or sets whether the document is created by the editor: its file is written at the next save.
    /// </summary>
    bool IsNew { get; set; }

    /// <summary>
    /// Gets or sets whether the file changed on disk since it was read or saved.
    /// </summary>
    bool ChangedOnDisk { get; set; }

    /// <summary>
    /// Gets or sets the number of errors of its view.
    /// </summary>
    int Errors { get; set; }

    /// <summary>
    /// Gets or sets the number of warnings of its view.
    /// </summary>
    int Warnings { get; set; }
}

/// <summary>
/// A property of a view.
/// </summary>
public interface IEditorNode : IPoco
{
    /// <summary>
    /// Gets or sets the path of the property (its names separated by ':').
    /// </summary>
    string Path { get; set; }

    /// <summary>
    /// Gets or sets the name of the property.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the depth of the property (0 for a property of the root object).
    /// </summary>
    int Depth { get; set; }

    /// <summary>
    /// Gets or sets the kind of the value: "Object", "Array", "String", "Number", "Boolean" or "Null".
    /// </summary>
    string Kind { get; set; }

    /// <summary>
    /// Gets or sets the JSON text of the value, except for an object (null): its properties are the next nodes.
    /// </summary>
    string? Value { get; set; }

    /// <summary>
    /// Gets or sets the comments written on the lines above the property (their text, without the comment markers), or null.
    /// </summary>
    string? Comment { get; set; }

    /// <summary>
    /// Gets or sets what the property is in the configuration, given by the catalog: "Builder", "BuilderAspect",
    /// "BuilderAspectOption", "App", "AppAspect", "AppAspectOption", "Party", "PartyDefaults", "PartyFeature",
    /// "PartyFeatureOption", "Client", "ClientFeature", "ClientFeatureOption", or null (a key of a section, or no catalog).
    /// </summary>
    string? Role { get; set; }

    /// <summary>
    /// Gets or sets the configuration name of the configurable type of the property (its documentation is in the
    /// catalog), or null.
    /// </summary>
    string? TypeName { get; set; }

    /// <summary>
    /// Gets or sets where the property comes from in the view of a deployment (null in the central view): "Central"
    /// (inherited from the central configuration), the name of a base deployment (inherited from it), "Deployment"
    /// (written by the overlay of the deployment: a value, or an object that it writes in, whose properties have their own
    /// origin) or "Removed" (a <c>null</c> of the overlay removes it).
    /// </summary>
    string? Origin { get; set; }
}

/// <summary>
/// A problem of a view.
/// </summary>
public interface IEditorProblem : IPoco
{
    /// <summary>
    /// Gets or sets the path of the property that has the problem (empty for the document).
    /// </summary>
    string Path { get; set; }

    /// <summary>
    /// Gets or sets the severity: "Error", "Warning" or "Info".
    /// </summary>
    string Severity { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    string Message { get; set; }

    /// <summary>
    /// Gets or sets the layer of the validation: "Static" (the rules of the configuration, from the catalog, at every
    /// edit) or "Build" (the check of the saved configuration by the builders of the solution).
    /// </summary>
    string Layer { get; set; }
}
