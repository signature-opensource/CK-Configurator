using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CatalogModel = CK.Configurator.Catalog.Catalog;

namespace CK.Configurator;

/// <summary>
/// The effective values of the absent keys of the modeled sections of a view: a key that a section doesn't write takes
/// its value from the nearest level that has it (<see cref="EditorLevels"/>: key by key, as CK.ConfigurationModel's reader
/// does), otherwise the default of its model. The keys of the nested models are followed; the entries of a dictionary are
/// not (a named entry is a value of its level).
/// </summary>
static class EditorDefaults
{
    /// <summary>
    /// Computes the values that the absent keys of the modeled sections take from another level.
    /// </summary>
    /// <param name="nodes">The typed properties of the view.</param>
    /// <param name="catalog">The catalog.</param>
    /// <returns>The values taken from another level.</returns>
    public static List<EditorDefaultedValueData> Compute( IReadOnlyList<EditorNodeData> nodes, CatalogModel catalog )
    {
        var result = new List<EditorDefaultedValueData>();
        var byPath = new Dictionary<string, EditorNodeData>( StringComparer.OrdinalIgnoreCase );
        foreach( var n in nodes ) byPath.TryAdd( n.Path, n );
        foreach( var n in nodes )
        {
            if( n.TypeName == null || n.Kind is not ("Object" or "Boolean") || n.Value == "false" ) continue;
            var schema = EditorAnnotations.FindType( catalog, n.TypeName, n.Role )?.ConfigurationSchema;
            if( schema == null ) continue;
            Walk( n.Path, schema, schema["$defs"] as JsonObject, byPath, result, [] );
        }
        return result;
    }

    static void Walk( string path, JsonObject model, JsonObject? defs, Dictionary<string, EditorNodeData> byPath, List<EditorDefaultedValueData> result, List<string> visiting )
    {
        if( model["properties"] is not JsonObject properties ) return;
        foreach( var (name, value) in properties )
        {
            if( value is not JsonObject p ) continue;
            var keyPath = $"{path}:{name}";
            if( p["$ref"]?.GetValue<string>() is string r && r.StartsWith( "#/$defs/", StringComparison.Ordinal ) )
            {
                var def = r.Substring( 8 );
                if( defs?[def] is JsonObject nested && !visiting.Contains( def ) ) Walk( keyPath, nested, defs, byPath, result, [.. visiting, def] );
                continue;
            }
            if( byPath.ContainsKey( keyPath ) ) continue;
            foreach( var level in EditorLevels.Of( keyPath ).Skip( 1 ) )
            {
                if( byPath.TryGetValue( level, out var d ) && d.Kind != "Null" )
                {
                    result.Add( new EditorDefaultedValueData( keyPath, d.Value, level ) );
                    break;
                }
            }
        }
    }
}

/// <summary>
/// The value that an absent key of a modeled section takes from another level (the "Defaults").
/// </summary>
/// <param name="Path">The path of the absent key.</param>
/// <param name="Value">The JSON text of the value (null for a section).</param>
/// <param name="From">The path of the property that gives it.</param>
public sealed record EditorDefaultedValueData( string Path, string? Value, string From );
