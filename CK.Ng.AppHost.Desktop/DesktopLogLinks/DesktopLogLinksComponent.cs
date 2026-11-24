using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.AppHost.Desktop;

/// <summary>
/// The "Last log" and "Open folder" links of a desktop application (<c>ck-desktop-log-links</c>): shown only when the
/// application runs with its window and writes a text log. "Last log" closes the current text log and opens it,
/// "Open folder" opens the folder of the text logs.
/// </summary>
[NgComponent]
[Package<NgAppHostDesktopPackage>]
public sealed class DesktopLogLinksComponent : NgComponent
{
}
