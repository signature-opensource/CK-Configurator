using CK.Core;
using CK.IO.Configurator;
using CK.Ng.Cris.AspNet;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Configurator;

/// <summary>
/// The configurator's client side: its catalog page (<see cref="CatalogPageComponent"/>) and the commands it sends
/// (CK.IO.Configurator, handled by CK.Configurator).
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetPackage, ZorroPackage>]
[RegisterTypeScriptType( typeof( IOpenCatalogCommand ) )]
[RegisterTypeScriptType( typeof( IGetConfigurableTypesQCommand ) )]
[RegisterTypeScriptType( typeof( IGetClosureQCommand ) )]
[RegisterTypeScriptType( typeof( IGetCommandsQCommand ) )]
public class NgConfiguratorPackage : TypeScriptPackage
{
}
