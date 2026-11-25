using CK.Core;
using CK.IO.AppHost.Desktop;
using CK.IO.Configurator;
using CK.Ng.Cris.AspNet;
using CK.Ng.Markdown;
using CK.Ng.Zorro;
using CK.TypeScript;

namespace CK.Ng.Configurator;

/// <summary>
/// The configurator's client side: the opened catalog of the application (<c>CatalogSource</c>, a root service) and its
/// picker (<see cref="CatalogSourcePickerComponent"/>), the catalog page (<see cref="CatalogPageComponent"/>, at
/// "ckomposable/catalog"), the editor page (<see cref="EditorPageComponent"/>, at "ckomposable/editor") and the commands
/// they send (CK.IO.Configurator, handled by CK.Configurator; the folder dialog of a desktop window, handled by
/// CK.AppHost.Desktop).
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
[RegisterTypeScriptType( typeof( IOpenSolutionCommand ) )]
[RegisterTypeScriptType( typeof( IGetEditorStateQCommand ) )]
[RegisterTypeScriptType( typeof( ICloseSolutionCommand ) )]
[RegisterTypeScriptType( typeof( ISetValueCommand ) )]
[RegisterTypeScriptType( typeof( IAddPropertyCommand ) )]
[RegisterTypeScriptType( typeof( IRemovePropertyCommand ) )]
[RegisterTypeScriptType( typeof( IRevertPropertyCommand ) )]
[RegisterTypeScriptType( typeof( ICreateDeploymentCommand ) )]
[RegisterTypeScriptType( typeof( ICheckSolutionCommand ) )]
[RegisterTypeScriptType( typeof( ISetCatalogSourceCommand ) )]
[RegisterTypeScriptType( typeof( IUndoCommand ) )]
[RegisterTypeScriptType( typeof( IRedoCommand ) )]
[RegisterTypeScriptType( typeof( ISaveCommand ) )]
[RegisterTypeScriptType( typeof( IReloadCommand ) )]
[RegisterTypeScriptType( typeof( IPickFolderCommand ) )]
public class NgConfiguratorPackage : TypeScriptPackage
{
}
