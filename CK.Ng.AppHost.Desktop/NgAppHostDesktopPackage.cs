using CK.Core;
using CK.IO.AppHost.Desktop;
using CK.Ng.Axios;
using CK.Ng.Cris.AspNet;
using CK.TS.Angular;
using CK.TypeScript;

namespace CK.Ng.AppHost.Desktop;

/// <summary>
/// The client side of a desktop application: the links of the desktop (<see cref="DesktopLogLinksComponent"/>) and the
/// commands they send (CK.IO.AppHost.Desktop, handled by CK.AppHost.Desktop).
/// <para>
/// It provides the axios instance of the application (the <c>AXIOS</c> token of CK.Ng.Axios): when the page is served by
/// the "app" scheme of the desktop window (an in-process endpoint, no port), its requests are messages to the host
/// (<c>desktopAxiosAdapter</c>, relayed by the DesktopInProcessBridge of CK.AppHost.Desktop).
/// </para>
/// </summary>
[TypeScriptPackage]
[Requires<CrisAspNetPackage>]
[Requires<AxiosPackage>]
[TypeScriptFile( "desktop-axios.ts", "createDesktopAxios", "desktopAxiosAdapter" )]
[NgProviderImport( "AXIOS, createDesktopAxios" )]
[NgProvider( "{ provide: AXIOS, useFactory: () => createDesktopAxios() }" )]
[RegisterTypeScriptType( typeof( IGetDesktopInfoQCommand ) )]
[RegisterTypeScriptType( typeof( IOpenLastLogCommand ) )]
[RegisterTypeScriptType( typeof( IOpenLogFolderCommand ) )]
public class NgAppHostDesktopPackage : TypeScriptPackage
{
}
