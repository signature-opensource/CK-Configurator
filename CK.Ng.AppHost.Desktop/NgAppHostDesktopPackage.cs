using CK.Core;
using CK.IO.AppHost.Desktop;
using CK.Ng.Cris.AspNet;
using CK.TypeScript;

namespace CK.Ng.AppHost.Desktop;

/// <summary>
/// The client side of a desktop application: the links of the desktop (<see cref="DesktopLogLinksComponent"/>) and the
/// commands they send (CK.IO.AppHost.Desktop, handled by CK.AppHost.Desktop).
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetPackage>]
[RegisterTypeScriptType( typeof( IGetDesktopInfoQCommand ) )]
[RegisterTypeScriptType( typeof( IOpenLastLogCommand ) )]
[RegisterTypeScriptType( typeof( IOpenLogFolderCommand ) )]
public class NgAppHostDesktopPackage : TypeScriptPackage
{
}
