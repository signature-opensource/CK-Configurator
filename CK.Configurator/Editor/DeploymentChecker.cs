using CK.Configurator.Catalog;
using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// The static checks of the view of a deployment that are specific to it (the merged view is also checked by the
/// <see cref="ConfigurationChecker"/>): the keys that an overlay can't write, its <c>"$BASE"</c>, the builders of its
/// <c>"Deployment"</c> settings, and the values per deployment that it inherits instead of writing them (the rule of the
/// build, <see cref="CentralDeployment.FindInheritedValues"/>). The values per deployment are the ones of the
/// <c>"$PER_DEPLOYMENT"</c> markers and of the <c>[ConfigurationPerDeployment]</c> of the configurable types of the
/// catalog on the local party, its defaults and its clients; the ones that only the code declares (the contributors, the
/// remotes, the size of a fleet) are checked by the builders (the "Build" layer).
/// </summary>
static class DeploymentChecker
{
    static readonly JsonNodeOptions _nodeOptions = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonDocumentOptions _documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// A layer of a deployment: its name and the text of its overlay.
    /// </summary>
    public sealed record Layer( string Name, string Text );

    /// <summary>
    /// Checks the view of a deployment.
    /// </summary>
    /// <param name="centralText">The text of the central configuration.</param>
    /// <param name="bases">The overlays of the bases of the deployment, from the farthest to the nearest.</param>
    /// <param name="self">The overlay of the deployment.</param>
    /// <param name="selfTree">The tree of the overlay of the deployment.</param>
    /// <param name="nodes">The typed properties of the merged view.</param>
    /// <param name="catalog">The catalog, null when the document has none.</param>
    /// <param name="problems">Collects the problems.</param>
    public static void Check( string centralText,
                              IReadOnlyList<Layer> bases,
                              Layer self,
                              EditorTree selfTree,
                              IReadOnlyList<EditorNodeData> nodes,
                              CatalogModel? catalog,
                              List<EditorProblemData> problems )
    {
        foreach( var n in selfTree.Root )
        {
            if( n.Name.Equals( "Domain", StringComparison.OrdinalIgnoreCase ) || n.Name.Equals( "Environment", StringComparison.OrdinalIgnoreCase ) )
            {
                problems.Add( new EditorProblemData( n.Name, "Error", $"\"{n.Name}\" can't be written in a deployment: the domain and the environment of a deployment are its path in the \"{CentralDeployment.FolderName}\" folder." ) );
            }
            else if( n.Name.Equals( "GlobalDomainPrefix", StringComparison.OrdinalIgnoreCase ) )
            {
                problems.Add( new EditorProblemData( n.Name, "Error", $"\"{n.Name}\" can't be written in a deployment: it is how the solution names its projects, written in its central configuration only." ) );
            }
            else if( n.Name.Equals( "$configurator", StringComparison.OrdinalIgnoreCase ) || n.Name.Equals( "$schema", StringComparison.OrdinalIgnoreCase ) )
            {
                problems.Add( new EditorProblemData( n.Name, "Warning", $"\"{n.Name}\" is a key of the central configuration: a deployment doesn't need it." ) );
            }
        }
        CheckSettings( nodes, problems );
        CheckInherited( centralText, bases, self, nodes, catalog, problems );
    }

    // "Deployment:Builders": the paths of builders of the merged configuration.
    static void CheckSettings( IReadOnlyList<EditorNodeData> nodes, List<EditorProblemData> problems )
    {
        var settingsBuilders = nodes.FirstOrDefault( n => n.Path.Equals( $"{CentralDeployment.SettingsKey}:Builders", StringComparison.OrdinalIgnoreCase ) );
        if( settingsBuilders == null || settingsBuilders.Kind == "Null" ) return;
        var builders = nodes.Where( n => n.Depth == 1 && n.Path.StartsWith( "Builders:", StringComparison.OrdinalIgnoreCase ) ).Select( n => n.Name ).ToList();
        List<string?>? items = null;
        if( settingsBuilders.Kind == "Array" )
        {
            try
            {
                using var d = JsonDocument.Parse( settingsBuilders.Value!, _documentOptions );
                items = d.RootElement.EnumerateArray().Select( e => e.ValueKind == JsonValueKind.String ? e.GetString() : null ).ToList();
            }
            catch( JsonException )
            {
            }
        }
        if( items == null || items.Any( i => i == null ) )
        {
            problems.Add( new EditorProblemData( settingsBuilders.Path, "Error", $"\"{settingsBuilders.Path}\" is an array of the paths of the builders to deploy (all by default)." ) );
            return;
        }
        foreach( var i in items )
        {
            if( !builders.Contains( i!, StringComparer.OrdinalIgnoreCase ) )
            {
                problems.Add( new EditorProblemData( settingsBuilders.Path, "Error", $"Invalid builder '{i}' in \"{settingsBuilders.Path}\": the builders are '{string.Join( "', '", builders )}'." ) );
            }
        }
    }

    static void CheckInherited( string centralText,
                                IReadOnlyList<Layer> bases,
                                Layer self,
                                IReadOnlyList<EditorNodeData> nodes,
                                CatalogModel? catalog,
                                List<EditorProblemData> problems )
    {
        // The markers are read (and removed) as the build does; their syntax is checked by the ConfigurationChecker.
        var monitor = new ActivityMonitor( ActivityMonitorOptions.SkipAutoConfiguration );
        var markers = new List<PerDeploymentMarker>();
        var merged = Read( monitor, centralText, $"'{CentralConfiguration.FileName}'", markers );
        JsonObject? overlay = null;
        foreach( var layer in bases.Append( self ) )
        {
            var o = Read( monitor, layer.Text, $"'{CentralDeployment.FolderName}/{layer.Name}/{CentralConfiguration.FileName}'", markers );
            if( o == null ) return;
            foreach( var key in o.Select( kv => kv.Key ).Where( k => k.Equals( CentralDeployment.BaseKey, StringComparison.OrdinalIgnoreCase ) ).ToList() )
            {
                o.Remove( key );
            }
            merged = CentralDeployment.MergePatch( merged, o ) as JsonObject;
            overlay = o;
        }
        if( merged == null || overlay == null ) return;
        var patterns = markers.Select( m => new PerDeploymentPattern( m.ObjectPath, m.Pattern, $"\"{CentralDeployment.PerDeploymentKey}\" of {m.Source}" ) ).ToList();
        if( catalog != null ) patterns.AddRange( GetTypePatterns( nodes, catalog ) );
        var inherited = CentralDeployment.FindInheritedValues( merged, overlay, patterns, out _ );
        if( inherited.Count == 0 ) return;
        var paths = new HashSet<string>( nodes.Select( n => n.Path ), StringComparer.OrdinalIgnoreCase );
        foreach( var v in inherited )
        {
            problems.Add( new EditorProblemData( NearestNode( paths, v.Path ),
                                                 "Error",
                                                 $"The deployment '{self.Name}' inherits \"{v.Path}\" ({v.Source}): a value per deployment is written by the deployment itself (null removes it)." ) );
        }
    }

    static JsonObject? Read( IActivityMonitor monitor, string text, string source, List<PerDeploymentMarker> markers )
    {
        try
        {
            if( JsonNode.Parse( text, _nodeOptions, _documentOptions ) is not JsonObject o ) return null;
            return PerDeploymentMarker.Extract( monitor, o, "", source, markers ) ? o : null;
        }
        catch( JsonException )
        {
            // A duplicate key: the JSON(C) analyzer of the editor accepts it, the build doesn't.
            return null;
        }
    }

    // The deepest property of the view on the path (an item of an array is in its array).
    static string NearestNode( HashSet<string> paths, string path )
    {
        var p = path;
        while( p.Length > 0 && !paths.Contains( p ) )
        {
            int i = p.LastIndexOf( ':' );
            p = i < 0 ? "" : p.Substring( 0, i );
        }
        return p;
    }

    // The [ConfigurationPerDeployment( "Party:Urls" )] of the configurable types, as the build declares them on an App
    // (AppBuilderConfiguration.CollectPerDeploymentKeys): the path in each anchor of the kind of parties (the local party
    // "Local" and its fallback "Defaults" for "Party" and "Local", each client of the local party for "Client"), then the
    // section of the type (an option: in the section of each of its owners), then the rest of the path.
    static IEnumerable<PerDeploymentPattern> GetTypePatterns( IReadOnlyList<EditorNodeData> nodes, CatalogModel catalog )
    {
        var types = catalog.Packages.SelectMany( p => p.Assemblies ).SelectMany( a => a.Types )
                           .Where( t => t.Configurable != null && t.PerDeployment.Count > 0 )
                           .ToList();
        if( types.Count == 0 ) yield break;
        foreach( var app in nodes.Where( n => n.Role == "App" && n.Path.StartsWith( "Builders:", StringComparison.OrdinalIgnoreCase ) ) )
        {
            var clients = nodes.Where( n => n.Role == "Client" && n.Path.StartsWith( app.Path + ":", StringComparison.OrdinalIgnoreCase ) )
                               .Select( n => n.Path.Substring( app.Path.Length + 1 ) )
                               .ToList();
            foreach( var t in types )
            {
                var sections = GetSections( t, catalog );
                foreach( var declaration in t.PerDeployment )
                {
                    var parts = declaration.Split( ':', 2 );
                    if( parts.Length != 2 || parts[0].Length == 0 ) continue;
                    IEnumerable<string> anchors = parts[0] switch
                    {
                        "Party" or "Local" => ["Local", "Defaults"],
                        "Client" => clients,
                        _ => []
                    };
                    var source = $"[ConfigurationPerDeployment( \"{declaration}\" )] of '{t.CSharpName}'";
                    foreach( var anchor in anchors )
                    {
                        foreach( var section in sections )
                        {
                            var pattern = parts[1].Length == 0 ? $"{anchor}:{section}" : $"{anchor}:{section}:{parts[1]}";
                            yield return new PerDeploymentPattern( app.Path, pattern, source, IsApp: true );
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
