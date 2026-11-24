using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The opened catalog on one line, "[Stack url - World - profile]" (or "&lt;no catalog&gt;"), and its "Edit" button: a
/// dialog shows the picker (<see cref="CatalogSourcePickerComponent"/>) and closes once a catalog is opened. The shell
/// of an application shows it in its header.
/// </summary>
[NgComponent]
[Package<NgConfiguratorPackage>]
public sealed class CatalogSourceSelectorComponent : NgComponent
{
}
