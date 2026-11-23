using CK.Configurator.Catalog;
using CK.Core;
using CK.Cris;
using CK.IO.Configurator;
using CK.Testing;
using Microsoft.Extensions.Configuration;
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
    // A local Stack whose explo/uc CI profile has the packages of FixturePackages (at the versions that this project
    // references); the local feed of the Stack (when it exists) has the ones that are not published yet.
    static string CreateStack()
    {
        var stack = Path.Combine( TestHelper.TestProjectFolder, "bin", "Stack" );
        var published = Path.Combine( stack, "Published" );
        Directory.CreateDirectory( Path.Combine( published, "explo", "uc" ) );
        File.WriteAllText( Path.Combine( published, "index.json" ), """
            { "Alive": { "explo/uc-ci": [ "2026.11.22-0.uc.0.ci.0" ] }, "Deprecated": { "explo/uc-ci": [ "2026.11.21-0.uc.0.ci.0" ] } }
            """ );
        var localFeed = FixturePackages.LocalFeed is string local ? $"{{ \"Name\": \"Local\", \"Url\": \"{local}\" }}," : "";
        File.WriteAllText( Path.Combine( published, "explo", "uc", "v2026.11.22-0.uc.0.ci.0.json" ), $$"""
            {
              "StackUrl": "https://github.com/signature-opensource/CK-Stack",
              "World": "CK",
              "Version": "2026.11.22-0.uc.0.ci.0",
              "IsDeprecated": false,
              "Repositories": [
                { "Url": "https://github.com/signature-opensource/CK-AppIdentity", "Id": "19avtIQaSUE",
                  "Packages": [ "{{FixturePackages.Pin( "CK.AppIdentity.PartyClient" )}}" ] },
                { "Url": "https://github.com/signature-opensource/CK-AppIdentity-Transport", "Id": "rzheU9-ntBE",
                  "Packages": [ "{{FixturePackages.Pin( "CK.AppIdentity.TransportLayer" )}}" ] },
                { "Url": "https://github.com/signature-opensource/CK-AspNet", "Id": "R-8R-4qqyVc",
                  "Packages": [ "{{FixturePackages.Pin( "CK.AppIdentity.HttpEndpoint" )}}" ] },
                { "Url": "https://github.com/signature-opensource/CK-Cris-Abstractions", "Id": "NuqgNBQFZL0",
                  "Packages": [ "{{FixturePackages.Pin( "CK.IO.DynamicConfiguration" )}}" ] },
                { "Url": "https://github.com/signature-opensource/CK-Globalization-Engine", "Id": "2fEdV71a5BA",
                  "Packages": [ "{{FixturePackages.Pin( "CK.Globalization.StObj" )}}" ] }
              ],
              "Feeds": [
                {{localFeed}}
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
                                              typeof( IGetBranchesQCommand ),
                                              typeof( IGetProfilesQCommand ),
                                              typeof( IBranchInfo ),
                                              typeof( IProfileInfo ),
                                              typeof( IGetWorldsQCommand ),
                                              typeof( IGetPackageQCommand ),
                                              typeof( IPackageInfo ),
                                              typeof( ICatalogInfo ),
                                              typeof( IConfigurableTypeInfo ),
                                              typeof( ILockInfo ),
                                              typeof( IPropertyLevelInfo ),
                                              typeof( ICommandInfo ) );
        // A development build: the workers reference the analyzer project (relative to the solution).
        var config = new ConfigurationBuilder().AddInMemoryCollection( new Dictionary<string, string?>
        {
            ["Configurator:CacheRoot"] = Path.Combine( TestHelper.TestProjectFolder, "bin", "Cache" ),
            ["Configurator:AnalyzerProject"] = "CK.Configurator.Catalog.Worker/CK.Configurator.Catalog.Worker.csproj"
        } ).Build();
        await using var auto = (await configuration.RunSuccessfullyAsync()).CreateAutomaticServices( configureServices: services =>
        {
            services.AddSingleton<IConfiguration>( config );
            services.AddScoped<IActivityMonitor>( s => TestHelper.Monitor );
        } );
        var catalogs = auto.Services.GetRequiredService<ConfiguratorCatalogService>();
        catalogs.Analyzer!.ProjectPath.ShouldBe( Path.GetFullPath( Path.Combine( TestHelper.SolutionFolder, "CK.Configurator.Catalog.Worker", "CK.Configurator.Catalog.Worker.csproj" ) ) );
        using var scope = auto.Services.CreateScope();
        var services = scope.ServiceProvider;

        // The local Stack has no LTS World: its default World only.
        var worlds = (IList<string>)(await ExecuteAsync<IGetWorldsQCommand>( services, c => c.World = stack ))!;
        worlds.ShouldBe( [Path.GetFullPath( stack )] );

        var branches = (IList<IBranchInfo>)(await ExecuteAsync<IGetBranchesQCommand>( services, c => c.World = stack ))!;
        var uc = branches.ShouldHaveSingleItem();
        uc.Name.ShouldBe( "explo/uc" );
        uc.Latest.Version.ShouldBe( "2026.11.22-0.uc.0.ci.0" );
        uc.Latest.Date.ShouldBe( "2026.11.22" );
        uc.Latest.IsCI.ShouldBeTrue();
        uc.Latest.HasFeeds.ShouldBeTrue();

        var profiles = (IList<IProfileInfo>)(await ExecuteAsync<IGetProfilesQCommand>( services, c =>
        {
            c.World = stack;
            c.Branch = "explo/uc";
            c.IncludeDeprecated = true;
        } ))!;
        // The deprecated profile is listed by the index. Its file is missing: it is listed without feeds (it can't be opened).
        profiles.Select( p => p.Version ).ShouldBe( ["2026.11.22-0.uc.0.ci.0", "2026.11.21-0.uc.0.ci.0"] );
        profiles[1].IsDeprecated.ShouldBeTrue();
        profiles[1].HasFeeds.ShouldBeFalse();

        void Source( ICommandCatalogSource s )
        {
            s.World = stack;
            s.Version = "2026.11.22-0.uc.0.ci.0";
        }

        var info = (ICatalogInfo)(await ExecuteAsync<IOpenCatalogCommand>( services, Source ))!;
        info.PackageCount.ShouldBe( 5 );

        var types = (IList<IConfigurableTypeInfo>)(await ExecuteAsync<IGetConfigurableTypesQCommand>( services, Source ))!;
        types.Count.ShouldBe( info.ConfigurableTypeCount );
        var http = types.Single( t => t.Name == "HttpEndpoint" );
        http.Kind.ShouldBe( "PartyFeature" );
        http.PackageId.ShouldBe( "CK.AppIdentity.HttpEndpoint" );
        http.PerDeployment.ShouldBe( ["Party:Urls"] );
        http.Summary.ShouldNotBeNullOrWhiteSpace();

        var package = (IPackageInfo)(await ExecuteAsync<IGetPackageQCommand>( services, c => { Source( c ); c.PackageId = "CK.AppIdentity.HttpEndpoint"; } ))!;
        package.Version.ShouldBe( FixturePackages.GetVersion( "CK.AppIdentity.HttpEndpoint" ) );
        package.Readme.ShouldNotBeNullOrWhiteSpace();
        types.Single( t => t.Name == "ForwardedHeaders" ).OptionOwners.ShouldBe( [http.TypeName] );
        http.ConfigurationSchema.ShouldBeNull();
        var globalization = types.Single( t => t.Name == "Globalization" );
        globalization.ConfigurationModel.ShouldBe( "CK.Globalization.GlobalizationAppAspectConfiguration" );
        globalization.ConfigurationSchema.ShouldNotBeNull().ShouldContain( "\"ActiveCultures\"" );

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
        commands.ShouldContain( c => c.Summary != null );

        // An unknown World or version is an error result, not an exception.
        var command = services.GetRequiredService<PocoDirectory>().Create<IOpenCatalogCommand>( c => { c.World = Path.Combine( stack, "Nope" ); c.Version = "2026.11.22-0.uc.0.ci.0"; } );
        (await services.GetRequiredService<RawCrisExecutor>().RawExecuteAsync( services, command )).Result.ShouldBeAssignableTo<ICrisResultError>();
        command = services.GetRequiredService<PocoDirectory>().Create<IOpenCatalogCommand>( c => { c.World = stack; c.Version = "2026.11.23-0.uc.0.ci.0"; } );
        (await services.GetRequiredService<RawCrisExecutor>().RawExecuteAsync( services, command )).Result.ShouldBeAssignableTo<ICrisResultError>();
    }
}
