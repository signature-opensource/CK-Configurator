using CK.Configurator.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// The declarations of the configurable types on the anchors of the Apps, as the build places them
/// (<c>AppContextConfiguration.CollectPerDeploymentKeys</c>, <c>ApplyLocks</c>): a declaration's path is the kind of the
/// parties, then the path in the section of the type ("Party:Urls"). Its pattern is, relative to the App, the anchor of
/// each party of the kind (the local party "Local" and its fallback "Defaults" for "Party" and "Local", each client of
/// the local party for "Client"; the remotes are not in the editor), then the section of the type (an option: in the
/// section of each of its owners), then the rest of the path.
/// </summary>
static class EditorAnchors
{
    /// <summary>
    /// A declaration of a type placed on an App.
    /// </summary>
    public sealed record Placed<T>( string AppPath, string Pattern, TypeAnalysis Type, string Declaration, T Item );

    /// <summary>
    /// Places the declarations of the types of the catalog on the Apps of the typed properties of a view.
    /// </summary>
    /// <param name="nodes">The typed properties.</param>
    /// <param name="catalog">The catalog.</param>
    /// <param name="select">The declarations of a type: their path and what they carry.</param>
    /// <returns>The placed declarations.</returns>
    public static IEnumerable<Placed<T>> Place<T>( IReadOnlyList<EditorNodeData> nodes,
                                                   CatalogModel catalog,
                                                   Func<TypeAnalysis, IEnumerable<(string Path, T Item)>> select )
    {
        var types = catalog.Packages.SelectMany( p => p.Assemblies ).SelectMany( a => a.Types )
                           .Where( t => t.Configurable != null )
                           .Select( t => (Type: t, Declarations: select( t ).ToList()) )
                           .Where( t => t.Declarations.Count > 0 )
                           .ToList();
        if( types.Count == 0 ) yield break;
        foreach( var app in nodes.Where( n => n.Role == "App" && n.Path.StartsWith( "AppContexts:", StringComparison.OrdinalIgnoreCase ) ) )
        {
            var clients = nodes.Where( n => n.Role == "Client" && n.Path.StartsWith( app.Path + ":", StringComparison.OrdinalIgnoreCase ) )
                               .Select( n => n.Path.Substring( app.Path.Length + 1 ) )
                               .ToList();
            foreach( var (t, declarations) in types )
            {
                var sections = GetSections( t, catalog );
                foreach( var (declaration, item) in declarations )
                {
                    var parts = declaration.Split( ':', 2 );
                    if( parts.Length != 2 || parts[0].Length == 0 ) continue;
                    IEnumerable<string> anchors = parts[0] switch
                    {
                        "Party" or "Local" => ["Local", "Defaults"],
                        "Client" => clients,
                        _ => []
                    };
                    foreach( var anchor in anchors )
                    {
                        foreach( var section in sections )
                        {
                            var pattern = parts[1].Length == 0 ? $"{anchor}:{section}" : $"{anchor}:{section}:{parts[1]}";
                            yield return new Placed<T>( app.Path, pattern, t, declaration, item );
                        }
                    }
                }
            }
        }
    }

    static IReadOnlyList<string> GetSections( TypeAnalysis t, CatalogModel catalog )
    {
        var name = t.Configurable!.Name;
        if( !t.Configurable.Kind.EndsWith( "Option", StringComparison.Ordinal ) ) return [name];
        return t.OptionOwners.Select( o => catalog.FindType( o )?.Type.Configurable?.Name )
                             .Where( o => o != null )
                             .Select( o => $"{o}:{name}" )
                             .ToList();
    }
}
