using CK.Core;
using CK.Cris;
using CK.IO.Configurator;
using System.Threading.Tasks;

namespace CK.Configurator;

/// <summary>
/// Handles the editor commands of the configurator (CK.IO.Configurator): each one returns the state of the document
/// (<see cref="ConfiguratorEditorService"/>). No right is checked: the configurator is a development tool that runs
/// locally.
/// </summary>
public sealed class ConfiguratorEditorHandlers : ISingletonAutoService
{
    static IEditorState ToState( PocoDirectory pocos, EditorSnapshot s )
    {
        return pocos.Create<IEditorState>( state =>
        {
            state.Folder = s.Folder;
            state.FilePath = s.FilePath;
            state.IsDirty = s.IsDirty;
            state.CanUndo = s.CanUndo;
            state.CanRedo = s.CanRedo;
            state.ChangedOnDisk = s.ChangedOnDisk;
            state.CatalogWorld = s.CatalogWorld;
            state.CatalogVersion = s.CatalogVersion;
            foreach( var n in s.Nodes )
            {
                state.Nodes.Add( pocos.Create<IEditorNode>( node =>
                {
                    node.Path = n.Path;
                    node.Name = n.Name;
                    node.Depth = n.Depth;
                    node.Kind = n.Kind;
                    node.Value = n.Value;
                    node.Comment = n.Comment;
                } ) );
            }
            foreach( var p in s.Problems )
            {
                state.Problems.Add( pocos.Create<IEditorProblem>( problem =>
                {
                    problem.Path = p.Path;
                    problem.Severity = p.Severity;
                    problem.Message = p.Message;
                } ) );
            }
        } );
    }

    /// <summary>
    /// Opens a solution.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IOpenSolutionCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.OpenAsync( monitor, command.Folder ).ConfigureAwait( false ) );

    /// <summary>
    /// Gets the state.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IGetEditorStateQCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.GetStateAsync().ConfigureAwait( false ) );

    /// <summary>
    /// Closes the solution.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( ICloseSolutionCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.CloseAsync().ConfigureAwait( false ) );

    /// <summary>
    /// Replaces a value.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, ISetValueCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.SetValueAsync( monitor, command.Path, command.Json ).ConfigureAwait( false ) );

    /// <summary>
    /// Adds a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IAddPropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.AddPropertyAsync( monitor, command.ObjectPath, command.Name, command.Json ).ConfigureAwait( false ) );

    /// <summary>
    /// Removes a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IRemovePropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.RemovePropertyAsync( monitor, command.Path ).ConfigureAwait( false ) );

    /// <summary>
    /// Undoes the last edit.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IUndoCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.UndoAsync().ConfigureAwait( false ) );

    /// <summary>
    /// Redoes the last undone edit.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IRedoCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.RedoAsync().ConfigureAwait( false ) );

    /// <summary>
    /// Saves the document.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, ISaveCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.SaveAsync( monitor, command.Overwrite ).ConfigureAwait( false ) );

    /// <summary>
    /// Reloads the file.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IReloadCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.ReloadAsync( monitor ).ConfigureAwait( false ) );
}
