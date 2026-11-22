using CK.Core;
using CK.TS.Angular;

namespace CK.Ng.Configurator;

/// <summary>
/// The catalog page, at the root of the client application: a source (a Stack, a branch, its CI builds) is opened, its
/// configurable types are listed (the options under their owners), a selection shows what it brings in (its closure),
/// and the commands are listed.
/// </summary>
[NgRoutedComponent<AppComponent>( Route = "" )]
[Package<NgConfiguratorPackage>]
public sealed class CatalogPageComponent : NgRoutedComponent
{
}
