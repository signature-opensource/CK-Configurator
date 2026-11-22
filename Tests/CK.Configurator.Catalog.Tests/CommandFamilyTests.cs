using CK.Core;
using CK.Packaging.Abstractions;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Text;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Catalog.Tests;

[TestFixture]
public class CommandFamilyTests
{
    const string _profile = """
        {
          "StackUrl": "https://github.com/signature-opensource/CK-Stack",
          "World": "CK",
          "Version": "2026.11.22",
          "IsDeprecated": false,
          "Repositories": [
            { "Url": "https://github.com/signature-opensource/A", "Id": "K-pyHHOdeTo", "Packages": [ "A@1.0.0" ] },
            { "Url": "https://github.com/signature-opensource/B", "Id": "aonVNTKApL8", "Packages": [ "B@1.0.0" ] }
          ]
        }
        """;

    static TypeAnalysis Command( string fullName, bool isAbstract, string exposure = "Public", string? externalName = null, params string[] bases )
    {
        return new TypeAnalysis
        {
            FullName = fullName,
            CSharpName = fullName,
            Command = new CommandDeclaration( isAbstract, bases, exposure, externalName )
        };
    }

    static PackageAnalysis Package( string id, params TypeAnalysis[] types )
    {
        return new PackageAnalysis
        {
            PackageId = id,
            Version = "1.0.0",
            Assemblies = [new AssemblyAnalysis { Name = id, Role = AssemblyRole.Model, Types = types }]
        };
    }

    static Catalog LoadCatalog( string testName, PackageAnalysis a, PackageAnalysis b )
    {
        var root = Path.Combine( TestHelper.TestProjectFolder, "bin", "FamilyTests", testName );
        if( Directory.Exists( root ) ) Directory.Delete( root, recursive: true );
        var cache = new CatalogCache( root );
        cache.Save( a );
        cache.Save( b );
        var source = new CatalogSource( PublishedProfile.Parse( Encoding.UTF8.GetBytes( _profile ) ), [] );
        return Catalog.Load( TestHelper.Monitor, source, cache ).ShouldNotBeNull();
    }

    [Test]
    public void a_family_spans_packages_and_takes_the_most_restrictive_exposure()
    {
        // A declares the abstract "Critical" part (Internal) and the primary command; B extends it.
        var a = Package( "A",
                         Command( "A.ICriticalPart", isAbstract: true, exposure: "Internal", bases: "CK.Cris.ICommandPart" ),
                         Command( "A.IDoCommand", isAbstract: false, externalName: "Do", bases: ["CK.Cris.ICommand<CK.Cris.ICrisBasicCommandResult>"] ) );
        var b = Package( "B",
                         Command( "B.IDoMoreCommand", isAbstract: false, bases: ["A.IDoCommand", "A.ICriticalPart"] ),
                         Command( "B.IOtherCommand", isAbstract: false, bases: "CK.Cris.ICommand" ) );
        var catalog = LoadCatalog( nameof( a_family_spans_packages_and_takes_the_most_restrictive_exposure ), a, b );

        catalog.Commands.Keys.Order().ShouldBe( ["B.IOtherCommand", "Do"] );
        var doCommand = catalog.Commands["Do"];
        doCommand.Primary.Type.FullName.ShouldBe( "A.IDoCommand" );
        doCommand.Interfaces.Select( i => $"{i.Package.PackageId}:{i.Type.FullName}" ).ShouldBe( ["A:A.IDoCommand", "B:B.IDoMoreCommand"] );
        doCommand.Exposure.ShouldBe( "Internal" );
        catalog.FindCommandByInterface( "B.IDoMoreCommand" ).ShouldBeSameAs( doCommand );
        catalog.FindCommandByInterface( "A.ICriticalPart" ).ShouldBeNull();

        catalog.Commands["B.IOtherCommand"].Exposure.ShouldBe( "Public" );
    }

    [Test]
    public void private_wins_and_an_interface_of_two_families_is_ignored()
    {
        var a = Package( "A",
                         Command( "A.ICommand1", isAbstract: false ),
                         Command( "A.ICommand2", isAbstract: false, exposure: "Internal" ) );
        var b = Package( "B",
                         Command( "B.ICommand1Private", isAbstract: false, exposure: "Private", bases: "A.ICommand1" ),
                         Command( "B.IBoth", isAbstract: false, bases: ["A.ICommand1", "A.ICommand2"] ) );
        var catalog = LoadCatalog( nameof( private_wins_and_an_interface_of_two_families_is_ignored ), a, b );

        catalog.Commands["A.ICommand1"].Exposure.ShouldBe( "Private" );
        catalog.Commands["A.ICommand2"].Exposure.ShouldBe( "Internal" );
        catalog.FindCommandByInterface( "B.IBoth" ).ShouldBeNull();
    }
}
