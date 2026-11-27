using CK.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CK.Configurator;

/// <summary>
/// The checks of a modeled section against the JSON Schema of its model (CK.ConfigurationModel's
/// <c>ConfigurationModelSchema</c>, the subset that it writes): an unknown key is an error (the model is closed: the
/// reader refuses it), and so are a value of the wrong type, a value outside its <c>enum</c>, a time span, a uuid or an uri
/// that doesn't parse, and a missing required key (looked up along the levels of the section: <see cref="EditorLevels"/>).
/// The keys that the model reserves (the names of the options of its owner) and the <c>$</c> keys are not checked here. A
/// handle (<c>{ "$SECRET": "..." }</c>) is accepted for any value; an opaque property (an external <c>$ref</c>) is not
/// checked. What only code checks (the model's <c>Validate</c>) is the builders' (the "Build" layer).
/// </summary>
sealed class SchemaChecker
{
    readonly Func<string, IReadOnlyList<EditorNodeData>> _children;
    readonly Func<string, EditorNodeData?> _find;
    readonly Action<string, string> _error;

    /// <summary>
    /// Initializes a new checker.
    /// </summary>
    /// <param name="children">The properties of an object, by its path.</param>
    /// <param name="find">A property by its path.</param>
    /// <param name="error">Reports an error on a path.</param>
    public SchemaChecker( Func<string, IReadOnlyList<EditorNodeData>> children, Func<string, EditorNodeData?> find, Action<string, string> error )
    {
        _children = children;
        _find = find;
        _error = error;
    }

    /// <summary>
    /// Checks a section.
    /// </summary>
    /// <param name="section">The property of the section (a section or <c>true</c>).</param>
    /// <param name="schema">The JSON Schema of the model.</param>
    /// <param name="typeName">The name of the configurable type (for the messages).</param>
    /// <param name="reserved">The keys that the model leaves to others (the options of its owner).</param>
    /// <param name="checkRequired">
    /// False for a section of a "Defaults" level: the defaults are partial, a required key is checked where it is used.
    /// </param>
    /// <param name="isDefaults">
    /// True for a section of a "Defaults" level: a key that is read on its owner's own section only (<c>x-ck-noDefaults</c>)
    /// is an error.
    /// </param>
    public void Check( EditorNodeData section, JsonObject schema, string typeName, IReadOnlySet<string> reserved, bool checkRequired, bool isDefaults = false )
    {
        _checkRequired = checkRequired;
        _isDefaults = isDefaults;
        var defs = schema["$defs"] as JsonObject;
        CheckModel( section.Path, section.Kind == "Object", schema, defs, typeName, reserved, isRoot: true );
    }

    bool _checkRequired;
    bool _isDefaults;

    /// <summary>
    /// Gets the names of the properties of a model's schema (empty for a null schema).
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The property names (case insensitive).</returns>
    public static HashSet<string> PropertyNames( JsonObject? schema )
    {
        var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        if( schema?["properties"] is JsonObject properties ) names.UnionWith( properties.Select( kv => kv.Key ) );
        return names;
    }

    void CheckModel( string path, bool isObject, JsonObject model, JsonObject? defs, string typeName, IReadOnlySet<string> reserved, bool isRoot )
    {
        var properties = model["properties"] as JsonObject ?? new JsonObject();
        if( isObject )
        {
            foreach( var c in _children( path ) )
            {
                if( c.Name.StartsWith( '$' ) || (isRoot && reserved.Contains( c.Name )) ) continue;
                var p = properties.FirstOrDefault( kv => kv.Key.Equals( c.Name, StringComparison.OrdinalIgnoreCase ) );
                if( p.Key == null )
                {
                    _error( c.Path, $"Unknown key '{c.Name}' in '{typeName}': its keys are {(properties.Count == 0 ? "none" : $"'{string.Join( "', '", properties.Select( kv => kv.Key ) )}'")}." );
                    continue;
                }
                if( isRoot && _isDefaults && p.Value is JsonObject nd && nd["x-ck-noDefaults"]?.GetValue<bool>() == true )
                {
                    _error( c.Path, $"'{c.Name}' can't be set in a \"Defaults\": '{typeName}' reads it on the party's own section only." );
                    continue;
                }
                if( p.Value is JsonObject ps ) CheckValue( c, ps, defs, typeName );
            }
        }
        if( _checkRequired && model["required"] is JsonArray required )
        {
            foreach( var name in required.Select( r => r?.GetValue<string>() ).Where( r => r != null ) )
            {
                if( !EditorLevels.Of( path ).Any( l => _find( $"{l}:{name}" ) is { Kind: not "Null" } ) )
                {
                    _error( path, $"'{name}' is required in '{typeName}'." );
                }
            }
        }
    }

    static JsonObject? Resolve( JsonObject s, JsonObject? defs )
    {
        if( s["$ref"]?.GetValue<string>() is string r && r.StartsWith( "#/$defs/", StringComparison.Ordinal ) )
        {
            return defs?[r.Substring( 8 )] as JsonObject;
        }
        return null;
    }

    void CheckValue( EditorNodeData n, JsonObject p, JsonObject? defs, string typeName )
    {
        if( n.Kind == "Null" ) return;
        // A handle stands for any value.
        if( n.Kind == "Object" )
        {
            var children = _children( n.Path );
            if( children.Count == 1 && ConfigurationSecret.IsHandleKey( children[0].Name, out _ ) ) return;
        }
        var nested = Resolve( p, defs );
        if( nested != null )
        {
            if( n.Kind != "Object" ) _error( n.Path, $"'{n.Name}' is a section." );
            else CheckModel( n.Path, true, nested, defs, typeName, new HashSet<string>(), isRoot: false );
            return;
        }
        // An opaque property: its own (external) schema.
        if( p["$ref"] != null ) return;
        if( p["anyOf"] is JsonArray )
        {
            // A string list: an array of strings or a comma separated string.
            if( n.Kind != "String" && !IsArrayOf( n, "string" ) ) _error( n.Path, $"'{n.Name}' is a list of strings (or a comma separated string)." );
            return;
        }
        switch( p["type"]?.GetValue<string>() )
        {
            case "boolean":
                if( n.Kind != "Boolean" ) _error( n.Path, $"'{n.Name}' is true or false." );
                break;
            case "integer":
                if( n.Kind != "Number" || !long.TryParse( n.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _ ) ) _error( n.Path, $"'{n.Name}' is an integer." );
                break;
            case "number":
                if( n.Kind != "Number" ) _error( n.Path, $"'{n.Name}' is a number." );
                break;
            case "string":
                CheckString( n, p );
                break;
            case "array":
                if( n.Kind != "Array" ) _error( n.Path, $"'{n.Name}' is a list." );
                else if( p["items"] is JsonObject items && items["type"]?.GetValue<string>() is string itemType && !IsArrayOf( n, itemType ) )
                {
                    _error( n.Path, $"'{n.Name}' is a list of {itemType}." );
                }
                break;
            case "object":
                if( n.Kind != "Object" ) _error( n.Path, $"'{n.Name}' is a section." );
                else if( p["additionalProperties"] is JsonObject entry )
                {
                    // A dictionary of models, or of values.
                    var entryModel = Resolve( entry, defs );
                    foreach( var e in _children( n.Path ) )
                    {
                        if( entryModel == null ) CheckValue( e, entry, defs, typeName );
                        else if( e.Kind == "Object" ) CheckModel( e.Path, true, entryModel, defs, typeName, new HashSet<string>(), isRoot: false );
                        else if( e.Kind != "Null" ) _error( e.Path, $"'{e.Name}' is a section." );
                    }
                }
                break;
        }
    }

    void CheckString( EditorNodeData n, JsonObject p )
    {
        if( n.Kind != "String" )
        {
            _error( n.Path, $"'{n.Name}' is a string." );
            return;
        }
        var value = JsonSerializer.Deserialize<string>( n.Value! ) ?? "";
        if( p["enum"] is JsonArray values )
        {
            var names = values.Select( v => v?.GetValue<string>() ).ToList();
            if( !names.Contains( value, StringComparer.OrdinalIgnoreCase ) ) _error( n.Path, $"'{n.Name}' is one of '{string.Join( "', '", names )}'." );
        }
        else if( p["x-ck-format"]?.GetValue<string>() == "timespan" )
        {
            if( !TimeSpan.TryParse( value, CultureInfo.InvariantCulture, out _ ) ) _error( n.Path, $"'{n.Name}' is a time span (\"00:05:00\", \"1.00:00:00\")." );
        }
        else if( p["format"]?.GetValue<string>() == "uuid" )
        {
            if( !Guid.TryParse( value, out _ ) ) _error( n.Path, $"'{n.Name}' is a uuid." );
        }
        else if( p["format"]?.GetValue<string>() == "uri" )
        {
            if( !Uri.TryCreate( value, UriKind.RelativeOrAbsolute, out _ ) ) _error( n.Path, $"'{n.Name}' is an uri." );
        }
    }

    static bool IsArrayOf( EditorNodeData n, string itemType )
    {
        if( n.Kind != "Array" ) return false;
        try
        {
            using var d = JsonDocument.Parse( n.Value!, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } );
            return d.RootElement.EnumerateArray().All( e => itemType switch
            {
                "string" => e.ValueKind == JsonValueKind.String,
                "boolean" => e.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" or "number" => e.ValueKind == JsonValueKind.Number,
                "object" => e.ValueKind == JsonValueKind.Object,
                _ => true
            } );
        }
        catch( JsonException )
        {
            return false;
        }
    }
}
