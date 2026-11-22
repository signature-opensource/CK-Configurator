using CK.Configurator.Catalog;
using CK.Core;
using CK.Cris;
using CK.IO.Configurator;
using CK.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shouldly;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Tests;

[TestFixture]
public class ConfiguratorCommandsTests
{
    // A local Stack whose explo/uc CI profile has 4 packages of the explo/uc CI profile of 2026-11-22.
    static string CreateStack()
    {
        var stack = Path.Combine( TestHelper.TestProjectFolder, "bin", "Stack" );
        var published = Path.Combine( stack, "Published" );
        Directory.CreateDirectory( Path.Combine( published, "explo", "uc" ) );
        File.WriteAllText( Path.Combine( published, "index.json" ), """
            { "Alive": { "explo/uc-ci": [ "2026.11.22-0.uc.0.ci.0" ] }, "Deprecated": {} }
            """ );
        File.WriteAllText( Path.Combine( published, "explo", "uc", "v2026.11.22-0.uc.0.ci.0.json" ), """
            {
              "StackUrl": "https://github.com/signature-opensource/CK-Stack",
              "World": "CK",
              "Version": "2026.11.22-0.uc.0.ci.0",
              "IsDeprecated": false,
              "Repositories": [
                { "Url": "https://github.com/signature-opensource/CK-AppIdentity", "Id": "19avtIQaSUE",
                  "Packages": [ "CK.AppIdentity.PartyClient@3.0.0-0.uc.0.ci.151" ] },
                { "Url": "https://github.com/signature-opensource/CK-AppIdentity-Transport", "Id": "rzheU9-ntBE",
                  "Packages": [ "CK.AppIdentity.TransportLayer@4.0.0-0.uc.0.ci.60" ] },
                { "Url": "https://github.com/signature-opensource/CK-AspNet", "Id": "R-8R-4qqyVc",
                  "Packages": [ "CK.AppIdentity.HttpEndpoint@25.0.0-0.uc.0.ci.49" ] },
                { "Url": "https://github.com/signature-opensource/CK-Cris-Abstractions", "Id": "NuqgNBQFZL0",
                  "Packages": [ "CK.IO.DynamicConfiguration@3.0.0-0.uc.0.ci.105" ] }
              ],
              "Feeds": [
                { "Name": "NuGet", "Url": "https://api.nuget.org/v3/index.json" },
                { "Name": "Signature-OpenSource", "Url": "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" }
              ]
            }
            """ );
        return stack;
    }

    static async Task<object?> ExecuteAsync<T>( System.IServiceProvider services, System.Action<T> configure ) where T : class, IAbstractCommand
    {
        var command = services.GetRequiredService<PocoDirectory>().Create( configure );
        var r = await services.GetRequiredService<RawCrisExecutor>().RawExecuteAsync( services, command );
        if( r.Result is ICrisResultError e ) Assert.Fail( $"{typeof( T ).Name} failed: {e.Errors.Select( m => m.Text ).Concatenate()}" );
        return r.Result;
    }

    [Test]
    public async Task the_catalog_commands_Async()
    {
        var stack = CreateStack();
        var configuration = TestHelper.CreateDefaultEngineConfiguration();
        configuration.FirstBinPath.Types.Add( typeof( RawCrisExecutor ),
                                              typeof( ConfiguratorCatalogService ),
                                              typeof( ConfiguratorCatalogHandlers ),
                                              typeof( IOpenCatalogCommand ),
                                              typeof( IGetConfigurableTypesQCommand ),
                                              typeof( IGetClosureQCommand ),
                                              typeof( IGetCommandsQCommand ),
                                              typeof( ICatalogInfo ),
                                              typeof( IConfigurableTypeInfo ),
                                              typeof( ILockInfo ),
                                              typeof( IPropertyLevelInfo ),
                                              typeof( ICommandInfo ) );
        await using var auto = (await configuration.RunSuccessfullyAsync()).CreateAutomaticServices( configureServices: services =>
        {
            services.AddScoped<IActivityMonitor>( s => TestHelper.Monitor );
        } );
        // A development build: the workers reference the analyzer project.
        var catalogs = auto.Services.GetRequiredService<ConfiguratorCatalogService>();
        catalogs.Cache = new CatalogCache( Path.Combine( TestHelper.TestProjectFolder, "bin", "Cache" ) );
        catalogs.Analyzer = AnalyzerReference.FromProject( Path.Combine( TestHelper.SolutionFolder,
                                                                         "CK.Configurator.Catalog.Worker",
                                                                         "CK.Configurator.Catalog.Worker.csproj" ) );
        using var scope = auto.Services.CreateScope();
        var services = scope.ServiceProvider;

        void Source( ICommandCatalogSource s )
        {
            s.Stack = stack;
            s.Branch = "explo/uc";
            s.IsCI = true;
        }

        var info = (ICatalogInfo)(await ExecuteAsync<IOpenCatalogCommand>( services, Source ))!;
        info.ProfileVersion.ShouldBe( "2026.11.22-0.uc.0.ci.0" );
        info.PackageCount.ShouldBe( 4 );

        var types = (IList<IConfigurableTypeInfo>)(await ExecuteAsync<IGetConfigurableTypesQCommand>( services, Source ))!;
        types.Count.ShouldBe( info.ConfigurableTypeCount );
        var http = types.Single( t => t.Name == "HttpEndpoint" );
        http.Kind.ShouldBe( "PartyFeature" );
        http.PackageId.ShouldBe( "CK.AppIdentity.HttpEndpoint" );
        http.PerDeployment.ShouldBe( ["Party:Urls"] );
        types.Single( t => t.Name == "ForwardedHeaders" ).OptionOwners.ShouldBe( [http.TypeName] );

        var kept = (IList<string>)(await ExecuteAsync<IGetClosureQCommand>( services, c =>
        {
            Source( c );
            c.Roots.Add( "CK.AppIdentity.TransportLayer.TransportFeatureDriver" );
            c.Packages.Add( "CK.AppIdentity.TransportLayer" );
            c.Packages.Add( "CK.AppIdentity.HttpEndpoint" );
        } ))!;
        kept.ShouldBe( ["CK.AppIdentity.TransportLayer.TransportFeatureDriver"] );

        var commands = (IList<ICommandInfo>)(await ExecuteAsync<IGetCommandsQCommand>( services, Source ))!;
        commands.Count.ShouldBe( info.CommandCount );
        commands.Select( c => c.PrimaryInterface ).ShouldContain( "CK.IO.DynamicConfiguration.IConfigurePropertyCommand" );
        commands.ShouldAllBe( c => c.Packages.Contains( "CK.IO.DynamicConfiguration" ) );

        // An unknown Stack is an error result, not an exception.
        var command = services.GetRequiredService<PocoDirectory>().Create<IOpenCatalogCommand>( c => { c.Stack = Path.Combine( stack, "Nope" ); c.Branch = "explo/uc"; } );
        (await services.GetRequiredService<RawCrisExecutor>().RawExecuteAsync( services, command )).Result.ShouldBeAssignableTo<ICrisResultError>();
    }
}
