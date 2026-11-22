using CK.Configurator.Catalog;
using CK.Core;
using CK.Cris;
using CK.IO.Configurator;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CK.Configurator;

/// <summary>
/// Handles the catalog commands of the configurator (CK.IO.Configurator). No right is checked: the configurator is a
/// development tool that runs locally (a hosting that exposes it adds the authentication to the commands).
/// </summary>
public sealed class ConfiguratorCatalogHandlers : ISingletonAutoService
{
    static async Task<ConfiguratorCatalogService.OpenedCatalog> OpenAsync( IActivityMonitor monitor,
                                                                          ICommandCatalogSource source,
                                                                          ConfiguratorCatalogService service )
    {
        return await service.OpenAsync( monitor, source.Stack, source.Branch, source.IsCI ).ConfigureAwait( false )
               ?? throw new CKException( $"Unable to open the catalog of '{source.Branch}{(source.IsCI ? " (CI)" : "")}' from '{source.Stack}' (see the logs)." );
    }

    /// <summary>
    /// Opens a catalog.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The command.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <returns>What the catalog is.</returns>
    [CommandHandler]
    public async Task<ICatalogInfo> HandleAsync( IActivityMonitor monitor, IOpenCatalogCommand command, ConfiguratorCatalogService service )
    {
        var c = (await OpenAsync( monitor, command, service ).ConfigureAwait( false )).Catalog;
        return command.CreateResult( r =>
        {
            r.ProfileVersion = c.Source.Profile.Version.ToString();
            r.PackageCount = c.Packages.Count;
            r.ConfigurableTypeCount = c.ConfigurableNames.Sum( n => c.FindConfigurable( n ).Count );
            r.CommandCount = c.Commands.Count;
        } );
    }

    /// <summary>
    /// Lists the configurable types of a catalog, ordered by name.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The configurable types.</returns>
    [CommandHandler]
    public async Task<IList<IConfigurableTypeInfo>> HandleAsync( IActivityMonitor monitor,
                                                                 IGetConfigurableTypesQCommand command,
                                                                 ConfiguratorCatalogService service,
                                                                 PocoDirectory pocos )
    {
        var c = (await OpenAsync( monitor, command, service ).ConfigureAwait( false )).Catalog;
        return c.ConfigurableNames.Order( System.StringComparer.OrdinalIgnoreCase )
                                  .SelectMany( c.FindConfigurable )
                                  .Select( t => ToInfo( pocos, t.Package, t.Type ) )
                                  .ToList();
    }

    static IConfigurableTypeInfo ToInfo( PocoDirectory pocos, PackageAnalysis package, TypeAnalysis type )
    {
        var configurable = type.Configurable!;
        return pocos.Create<IConfigurableTypeInfo>( i =>
        {
            i.Name = configurable.Name;
            i.Kind = configurable.Kind;
            i.Activation = configurable.Activation;
            i.TypeName = type.FullName;
            i.PackageId = package.PackageId;
            i.Section = type.Section;
            i.PerDeployment.AddRange( type.PerDeployment );
            i.Secrets.AddRange( type.Secrets );
            i.Locks.AddRange( type.Locks.Select( l => pocos.Create<ILockInfo>( x => { x.Path = l.Path; x.Level = l.Level; x.Value = l.Value; } ) ) );
            i.PropertyLevels.AddRange( type.PropertyLevels.Select( p => pocos.Create<IPropertyLevelInfo>( x => { x.Property = p.Property; x.Level = p.Level; } ) ) );
            i.OptionOwners.AddRange( type.OptionOwners );
        } );
    }

    /// <summary>
    /// Computes what a selection brings in (by the worker of the catalog).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <returns>The full names of the kept packages.</returns>
    [CommandHandler]
    public async Task<IList<string>> HandleAsync( IActivityMonitor monitor, IGetClosureQCommand command, ConfiguratorCatalogService service )
    {
        var opened = await OpenAsync( monitor, command, service ).ConfigureAwait( false );
        var server = await opened.GetServerAsync( monitor ).ConfigureAwait( false )
                     ?? throw new CKException( "The worker of the catalog failed to start (see the logs)." );
        var kept = await server.GetClosureAsync( monitor, command.Roots.ToList(), command.Packages.ToList() ).ConfigureAwait( false )
                   ?? throw new CKException( "The closure failed (see the logs)." );
        return kept.ToList();
    }

    /// <summary>
    /// Lists the commands of a catalog, ordered by name.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The commands.</returns>
    [CommandHandler]
    public async Task<IList<ICommandInfo>> HandleAsync( IActivityMonitor monitor,
                                                        IGetCommandsQCommand command,
                                                        ConfiguratorCatalogService service,
                                                        PocoDirectory pocos )
    {
        var c = (await OpenAsync( monitor, command, service ).ConfigureAwait( false )).Catalog;
        return c.Commands.Values
                         .OrderBy( f => f.Name, System.StringComparer.Ordinal )
                         .Select( f => pocos.Create<ICommandInfo>( i =>
                         {
                             i.Name = f.Name;
                             i.Exposure = f.Exposure;
                             i.PrimaryInterface = f.Primary.Type.FullName;
                             i.Packages.AddRange( f.Interfaces.Select( x => x.Package.PackageId ).Distinct() );
                         } ) )
                         .ToList();
    }
}
