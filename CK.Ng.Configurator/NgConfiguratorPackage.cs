using CK.Core;
using CK.IO.Configurator;
using CK.Ng.Cris.AspNet;
using CK.Ng.Markdown;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Configurator;

/// <summary>
/// The configurator's client side: the opened catalog of the application (<c>CatalogSource</c>, a root service) and its
/// picker (<see cref="CatalogSourcePickerComponent"/>), the catalog page (<see cref="CatalogPageComponent"/>, at
/// "ckomposable/catalog") and the commands they send (CK.IO.Configurator, handled by CK.Configurator).
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetPackage, ZorroPackage, MarkdownPackage>]
[TypeScriptFile( "catalog-source.ts", "CatalogSource" )]
[RegisterTypeScriptType( typeof( IGetWorldsQCommand ) )]
[RegisterTypeScriptType( typeof( IGetBranchesQCommand ) )]
[RegisterTypeScriptType( typeof( IGetProfilesQCommand ) )]
[RegisterTypeScriptType( typeof( IOpenCatalogCommand ) )]
[RegisterTypeScriptType( typeof( IGetConfigurableTypesQCommand ) )]
[RegisterTypeScriptType( typeof( IGetClosureQCommand ) )]
[RegisterTypeScriptType( typeof( IGetCommandsQCommand ) )]
[RegisterTypeScriptType( typeof( IGetPackageQCommand ) )]
[RegisterTypeScriptType( typeof( IGetPackagesQCommand ) )]
public class NgConfiguratorPackage : TypeScriptPackage
{
}
