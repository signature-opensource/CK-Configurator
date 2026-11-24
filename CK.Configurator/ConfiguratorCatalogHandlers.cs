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
        return await service.OpenAsync( monitor, source.World, source.Version ).ConfigureAwait( false )
               ?? throw new CKException( $"Unable to open the catalog of the profile '{source.Version}' of '{source.World}' (see the logs)." );
    }

    static void Fill( IProfileInfo info, CatalogProfileEntry entry )
    {
        var v = entry.Version;
        info.Version = v.ToString();
        info.Date = $"{v.Year}.{v.Month}.{v.Day}";
        info.ClashNumber = v.ClashNumber;
        info.IsCI = v.IsCI;
        info.IsDeprecated = entry.IsDeprecated;
        info.HasFeeds = entry.HasFeeds;
    }

    /// <summary>
    /// Lists the Worlds of a Stack.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <returns>The Worlds.</returns>
    [CommandHandler]
    public async Task<IList<string>> HandleAsync( IActivityMonitor monitor, IGetWorldsQCommand command, ConfiguratorCatalogService service )
    {
        var worlds = await service.GetWorldsAsync( monitor, command.World ).ConfigureAwait( false )
                     ?? throw new CKException( $"Unable to list the Worlds of '{command.World}' (see the logs)." );
        return worlds.Select( w => w.ToString() ).ToList();
    }

    /// <summary>
    /// Lists the branches of a World.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The branches.</returns>
    [CommandHandler]
    public async Task<IList<IBranchInfo>> HandleAsync( IActivityMonitor monitor,
                                                       IGetBranchesQCommand command,
                                                       ConfiguratorCatalogService service,
                                                       PocoDirectory pocos )
    {
        var branches = await service.GetBranchesAsync( monitor, command.World, command.IncludeDeprecated ).ConfigureAwait( false )
                       ?? throw new CKException( $"Unable to list the branches of '{command.World}' (see the logs)." );
        return branches.Select( b => pocos.Create<IBranchInfo>( i =>
                                {
                                    i.Name = b.Name;
                                    Fill( i.Latest, b.Latest );
                                } ) )
                       .ToList();
    }

    /// <summary>
    /// Lists the profiles of a branch of a World.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The profiles, the latest first.</returns>
    [CommandHandler]
    public async Task<IList<IProfileInfo>> HandleAsync( IActivityMonitor monitor,
                                                        IGetProfilesQCommand command,
                                                        ConfiguratorCatalogService service,
                                                        PocoDirectory pocos )
    {
        var profiles = await service.GetProfilesAsync( monitor, command.World, command.Branch, command.IncludeDeprecated ).ConfigureAwait( false )
                       ?? throw new CKException( $"Unable to list the profiles of '{command.Branch}' of '{command.World}' (see the logs)." );
        return profiles.Select( p => pocos.Create<IProfileInfo>( i => Fill( i, p ) ) ).ToList();
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

    static readonly System.Text.Json.JsonSerializerOptions _indented = new() { WriteIndented = true };

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
            i.Summary = type.Summary;
            i.Remarks = type.Remarks;
            i.ConfigurationModel = type.ConfigurationModel;
            i.ConfigurationSchema = type.ConfigurationSchema?.ToJsonString( _indented );
            i.SourceUrl = GetPageUrl( package, type, type.SourceUrl );
            i.ConfigurationModelSourceUrl = GetPageUrl( package, type, type.ConfigurationModelSourceUrl );
        } );
    }

    /// <summary>
    /// Gets the page of a source file of a type of a package (or of a file that it uses, like its configuration model).
    /// The tag of the version (CKli's "v{Version}") designates the commit of the package (the commit of the type): it is
    /// used for the files of that commit, the commit itself otherwise.
    /// </summary>
    static string? GetPageUrl( PackageAnalysis package, TypeAnalysis type, string? rawUrl )
    {
        if( !SourceFileUrl.TryParse( rawUrl, out var file ) ) return null;
        bool sameCommit = SourceFileUrl.TryParse( type.SourceUrl, out var source )
                          && source.Repository == file.Repository
                          && source.Commit == file.Commit;
        return file.GetPageUrl( sameCommit ? "v" + package.Version : null );
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
                             i.SourceUrl = GetPageUrl( f.Primary.Package, f.Primary.Type, f.Primary.Type.SourceUrl );
                             i.Packages.AddRange( f.Interfaces.Select( x => x.Package.PackageId ).Distinct() );
                             i.Summary = f.Primary.Type.Summary;
                             i.Remarks = f.Primary.Type.Remarks;
                         } ) )
                         .ToList();
    }

    /// <summary>
    /// Gets a package of a catalog.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <returns>The package.</returns>
    [CommandHandler]
    public async Task<IPackageInfo> HandleAsync( IActivityMonitor monitor, IGetPackageQCommand command, ConfiguratorCatalogService service )
    {
        var c = (await OpenAsync( monitor, command, service ).ConfigureAwait( false )).Catalog;
        var p = c.Packages.FirstOrDefault( p => p.PackageId.Equals( command.PackageId, System.StringComparison.OrdinalIgnoreCase ) )
                ?? throw new CKException( $"The package '{command.PackageId}' is not in the catalog of the profile '{command.Version}'." );
        return command.CreateResult( r =>
        {
            r.PackageId = p.PackageId;
            r.Version = p.Version;
            r.Readme = p.Readme;
        } );
    }

    /// <summary>
    /// Lists the packages of a catalog, ordered by identifier: their description, their direct dependencies and what they
    /// contain (configurable types, commands).
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="command">The query.</param>
    /// <param name="service">The opened catalogs.</param>
    /// <param name="pocos">The poco directory.</param>
    /// <returns>The packages.</returns>
    [CommandHandler]
    public async Task<IList<ICatalogPackageInfo>> HandleAsync( IActivityMonitor monitor,
                                                               IGetPackagesQCommand command,
                                                               ConfiguratorCatalogService service,
                                                               PocoDirectory pocos )
    {
        var c = (await OpenAsync( monitor, command, service ).ConfigureAwait( false )).Catalog;
        var types = c.ConfigurableNames.SelectMany( c.FindConfigurable )
                                       .ToLookup( t => t.Package.PackageId, t => t.Type.FullName, System.StringComparer.OrdinalIgnoreCase );
        // A command is in each package of its interfaces (its primary one, its parts).
        var commands = c.Commands.Values
                                 .SelectMany( f => f.Interfaces.Select( i => (i.Package.PackageId, f.Name) ).Distinct() )
                                 .ToLookup( x => x.PackageId, x => x.Name, System.StringComparer.OrdinalIgnoreCase );
        return c.Packages.OrderBy( p => p.PackageId, System.StringComparer.OrdinalIgnoreCase )
                         .Select( p => pocos.Create<ICatalogPackageInfo>( i =>
                         {
                             i.PackageId = p.PackageId;
                             i.Version = p.Version;
                             i.Description = p.Description;
                             i.HasReadme = p.Readme != null;
                             i.Dependencies.AddRange( p.Dependencies.Select( d => pocos.Create<IPackageDependencyInfo>( x =>
                             {
                                 x.PackageId = d.PackageId;
                                 x.VersionRange = d.VersionRange;
                             } ) ) );
                             i.ConfigurableTypes.AddRange( types[p.PackageId].Order( System.StringComparer.Ordinal ) );
                             i.Commands.AddRange( commands[p.PackageId].Order( System.StringComparer.Ordinal ) );
                         } ) )
                         .ToList();
    }
}
