using CK.Core;
using CK.Cris;
using CK.IO.Configurator;
using System.Threading.Tasks;

namespace CK.Configurator;

/// <summary>
/// Handles the editor commands of the configurator (CK.IO.Configurator): each one returns the state of a view
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
            state.View = s.View;
            state.FilePath = s.FilePath;
            state.IsDirty = s.IsDirty;
            state.CanUndo = s.CanUndo;
            state.CanRedo = s.CanRedo;
            state.ChangedOnDisk = s.ChangedOnDisk;
            state.CatalogWorld = s.CatalogWorld;
            state.CatalogVersion = s.CatalogVersion;
            state.CheckStatus = s.CheckStatus;
            state.CheckIsStale = s.CheckIsStale;
            foreach( var d in s.Documents )
            {
                state.Documents.Add( pocos.Create<IEditorDocument>( document =>
                {
                    document.Name = d.Name;
                    document.Base = d.Base;
                    document.FilePath = d.FilePath;
                    document.IsDirty = d.IsDirty;
                    document.IsNew = d.IsNew;
                    document.ChangedOnDisk = d.ChangedOnDisk;
                    document.Errors = d.Errors;
                    document.Warnings = d.Warnings;
                } ) );
            }
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
                    node.Role = n.Role;
                    node.TypeName = n.TypeName;
                    node.Origin = n.Origin;
                    node.LockLevel = n.LockLevel;
                    node.LockSource = n.LockSource;
                    node.CanUnlock = n.CanUnlock;
                    node.MaxLevel = n.MaxLevel;
                    node.SecretName = n.SecretName;
                    node.SecretStatus = n.SecretStatus;
                } ) );
            }
            foreach( var p in s.Problems )
            {
                state.Problems.Add( pocos.Create<IEditorProblem>( problem =>
                {
                    problem.Path = p.Path;
                    problem.Severity = p.Severity;
                    problem.Message = p.Message;
                    problem.Layer = p.Layer;
                    problem.Fix = p.Fix;
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
        => ToState( pocos, await editor.GetStateAsync( command.Deployment ).ConfigureAwait( false ) );

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
        => ToState( pocos, await editor.SetValueAsync( monitor, command.Deployment, command.Path, command.Json ).ConfigureAwait( false ) );

    /// <summary>
    /// Adds a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IAddPropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.AddPropertyAsync( monitor, command.Deployment, command.ObjectPath, command.Name, command.Json ).ConfigureAwait( false ) );

    /// <summary>
    /// Removes a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IRemovePropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.RemovePropertyAsync( monitor, command.Deployment, command.Path ).ConfigureAwait( false ) );

    /// <summary>
    /// Reverts a property of a deployment to the inherited one.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IRevertPropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.RevertPropertyAsync( monitor, command.Deployment, command.Path ).ConfigureAwait( false ) );

    /// <summary>
    /// Locks a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, ILockPropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.LockPropertyAsync( monitor, command.Deployment, command.Path, command.Level ).ConfigureAwait( false ) );

    /// <summary>
    /// Unlocks a property.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IUnlockPropertyCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.UnlockPropertyAsync( monitor, command.Deployment, command.Path ).ConfigureAwait( false ) );

    /// <summary>
    /// Removes an entry of a "$LOCK".
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IRemoveLockEntryCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.RemoveLockEntryAsync( monitor, command.Deployment, command.ObjectPath, command.Pattern ).ConfigureAwait( false ) );

    /// <summary>
    /// Makes a value a secret.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IMakeSecretCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.MakeSecretAsync( monitor, command.Deployment, command.Path, command.Name, command.IsOptional, command.Overwrite ).ConfigureAwait( false ) );

    /// <summary>
    /// Creates a deployment.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, ICreateDeploymentCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.CreateDeploymentAsync( monitor, command.Name, command.Base, command.WriteValuesPerDeployment ).ConfigureAwait( false ) );

    /// <summary>
    /// Checks the saved configuration with the builders.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( ICheckSolutionCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.CheckAsync( command.Deployment ).ConfigureAwait( false ) );

    /// <summary>
    /// Sets the source of the catalog of the document.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, ISetCatalogSourceCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.SetCatalogSourceAsync( monitor, command.Deployment, command.World, command.Version ).ConfigureAwait( false ) );

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
        => ToState( pocos, await editor.SaveAsync( monitor, command.Deployment, command.Overwrite ).ConfigureAwait( false ) );

    /// <summary>
    /// Reloads the file.
    /// </summary>
    [CommandHandler]
    public async Task<IEditorState> HandleAsync( IActivityMonitor monitor, IReloadCommand command, ConfiguratorEditorService editor, PocoDirectory pocos )
        => ToState( pocos, await editor.ReloadAsync( monitor, command.Deployment ).ConfigureAwait( false ) );
}
