using CK.Testing;
using NUnit.Framework;
using Shouldly;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Tests;

[TestFixture]
public class SolutionCheckRunnerTests
{
    static string CreateBuilder( string name, string? assets )
    {
        var folder = Path.Combine( TestHelper.TestProjectFolder, "bin", "CheckRunner", name );
        if( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
        Directory.CreateDirectory( Path.Combine( folder, "obj" ) );
        if( assets != null ) File.WriteAllText( Path.Combine( folder, "obj", "project.assets.json" ), assets );
        return folder;
    }

    [Test]
    public void a_builder_is_checked_only_when_its_CK_AppBuilder_has_the_check()
    {
        SolutionCheckRunner.FindAppBuilderSupport( CreateBuilder( "not-restored", null ) ).ShouldNotBeNull().ShouldContain( "is not restored" );
        SolutionCheckRunner.FindAppBuilderSupport( CreateBuilder( "no-reference", """{ "libraries": {}, "packageFolders": {} }""" ) )
                           .ShouldBe( "The builder doesn't reference CK.AppBuilder." );
        // A project reference is the current source.
        SolutionCheckRunner.FindAppBuilderSupport( CreateBuilder( "project", """{ "libraries": { "CK.AppBuilder/1.0.0": { "type": "project", "path": "../x.csproj" } }, "packageFolders": {} }""" ) )
                           .ShouldBeNull();
        // A package whose assembly has no CKomposableAppBuilder.CheckArgument (this test assembly stands for it).
        var packages = Path.Combine( TestHelper.TestProjectFolder, "bin", "CheckRunner", "packages" );
        var lib = Path.Combine( packages, "ck.appbuilder", "1.0.0", "lib", "net10.0" );
        Directory.CreateDirectory( lib );
        File.Copy( typeof( SolutionCheckRunnerTests ).Assembly.Location, Path.Combine( lib, "CK.AppBuilder.dll" ), overwrite: true );
        var assets = $$"""
            {
              "libraries": { "CK.AppBuilder/1.0.0": { "type": "package", "path": "ck.appbuilder/1.0.0", "files": [ "lib/net10.0/CK.AppBuilder.dll" ] } },
              "packageFolders": { {{System.Text.Json.JsonSerializer.Serialize( packages + Path.DirectorySeparatorChar )}}: {} }
            }
            """;
        SolutionCheckRunner.FindAppBuilderSupport( CreateBuilder( "old", assets ) ).ShouldNotBeNull().ShouldContain( "a newer version is needed" );
    }
}
