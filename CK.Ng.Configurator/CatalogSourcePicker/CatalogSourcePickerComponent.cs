using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The picker of the source of the opened catalog (<c>CatalogSource</c>): a World, one of its branches, one of its
/// profiles, then "Open". The shell of an application shows it in its header; otherwise the catalog page shows it.
/// </summary>
[NgComponent]
[Package<NgConfiguratorPackage>]
public sealed class CatalogSourcePickerComponent : NgComponent
{
}
