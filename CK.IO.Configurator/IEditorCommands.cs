using CK.Core;
using CK.Cris;
using System.Collections.Generic;

namespace CK.IO.Configurator;

/// <summary>
/// Opens the central configuration ("ckonf.json") of a solution: the document of the editor. The backend holds one
/// document: an opened one (and its unsaved edits) is closed.
/// </summary>
public interface IOpenSolutionCommand : ICommand<IEditorState>
{
    /// <summary>
    /// Gets or sets the folder of the solution: its "ckonf.json" is opened.
    /// </summary>
    string Folder { get; set; }
}

/// <summary>
/// Gets the state of the document (a closed state when no solution is opened).
/// </summary>
public interface IGetEditorStateQCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Closes the opened solution (its unsaved edits are lost).
/// </summary>
public interface ICloseSolutionCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Replaces the value of an existing property. The comments around it are kept.
/// </summary>
public interface ISetValueCommand : ICommand<IEditorState>
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
/// option or a feature is adding its key (<c>true</c> or a section).
/// </summary>
public interface IAddPropertyCommand : ICommand<IEditorState>
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
/// Removes a property with its comma, the comments above it and the comment that follows it on its line.
/// </summary>
public interface IRemovePropertyCommand : ICommand<IEditorState>
{
    /// <summary>
    /// Gets or sets the path of the property.
    /// </summary>
    string Path { get; set; }
}

/// <summary>
/// Undoes the last edit.
/// </summary>
public interface IUndoCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Redoes the last undone edit.
/// </summary>
public interface IRedoCommand : ICommand<IEditorState>
{
}

/// <summary>
/// Saves the document. When the file changed on disk since it was read (<see cref="IEditorState.ChangedOnDisk"/>), the
/// save is refused unless <see cref="Overwrite"/> is true.
/// </summary>
public interface ISaveCommand : ICommand<IEditorState>
{
    /// <summary>
    /// Gets or sets whether a file that changed on disk is overwritten.
    /// </summary>
    bool Overwrite { get; set; }
}

/// <summary>
/// Reads the file again: the edits (and their undo history) are lost.
/// </summary>
public interface IReloadCommand : ICommand<IEditorState>
{
}

/// <summary>
/// The state of the document, returned by every command of the editor.
/// </summary>
public interface IEditorState : IPoco
{
    /// <summary>
    /// Gets or sets the folder of the opened solution (null when no solution is opened).
    /// </summary>
    string? Folder { get; set; }

    /// <summary>
    /// Gets or sets the path of the edited file.
    /// </summary>
    string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets whether the document has unsaved edits.
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
    /// Gets or sets whether the file changed on disk (or disappeared) since it was read or saved: a save must
    /// overwrite it, or the document is reloaded.
    /// </summary>
    bool ChangedOnDisk { get; set; }

    /// <summary>
    /// Gets or sets the World of the catalog of the solution (its <c>"$configurator"</c> root key), or null.
    /// </summary>
    string? CatalogWorld { get; set; }

    /// <summary>
    /// Gets or sets the version of the catalog of the solution (its <c>"$configurator"</c> root key), or null.
    /// </summary>
    string? CatalogVersion { get; set; }

    /// <summary>
    /// Gets the properties of the document, in document order (a property is followed by the ones of its value).
    /// </summary>
    IList<IEditorNode> Nodes { get; }

    /// <summary>
    /// Gets the problems of the document.
    /// </summary>
    IList<IEditorProblem> Problems { get; }
}

/// <summary>
/// A property of the document.
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
}

/// <summary>
/// A problem of the document.
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
}
