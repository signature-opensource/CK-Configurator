using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The catalog page, at "ckomposable/catalog" (the "ckomposable/" prefix leaves the other routes to the application):
/// the opened catalog (<c>CatalogSource</c>) lists its configurable types (the options under their owners), a selection
/// shows what it brings in (its closure), its commands and its packages. Without a picker in the shell of the
/// application, the page shows its own.
/// </summary>
[NgRoutedComponent<AppComponent>( Route = "ckomposable/catalog" )]
[Package<NgConfiguratorPackage>]
public sealed class CatalogPageComponent : NgRoutedComponent
{
}
