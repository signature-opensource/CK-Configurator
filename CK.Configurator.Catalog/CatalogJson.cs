using System;
using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CK.Configurator.Catalog;

/// <summary>
/// The JSON of the catalog (source generated: no run time reflection). The empty lists and the null values are not
/// written.
/// </summary>
public static class CatalogJson
{
    static readonly JsonSerializerOptions _options = CreateOptions( indented: true );
    static readonly JsonSerializerOptions _compact = CreateOptions( indented: false );

    static JsonSerializerOptions CreateOptions( bool indented )
    {
        return new JsonSerializerOptions( CatalogJsonContext.Default.Options )
        {
            WriteIndented = indented,
            // The C# names of the generic types ("ICommand<TResult>") are written as is: the files are read by people too.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = CatalogJsonContext.Default.WithAddedModifier( SkipEmptyLists )
        };

        static void SkipEmptyLists( JsonTypeInfo typeInfo )
        {
            foreach( var p in typeInfo.Properties )
            {
                if( typeof( ICollection ).IsAssignableFrom( p.PropertyType )
                    || (p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof( System.Collections.Generic.IReadOnlyList<> )) )
                {
                    p.ShouldSerialize = static ( _, value ) => value is not ICollection { Count: 0 };
                }
            }
        }
    }

    /// <summary>
    /// Writes an analysis.
    /// </summary>
    /// <param name="analysis">The analysis.</param>
    /// <param name="indented">False for a compact JSON.</param>
    /// <returns>The utf-8 JSON.</returns>
    public static byte[] Write( PackageAnalysis analysis, bool indented = true )
    {
        ArgumentNullException.ThrowIfNull( analysis );
        return JsonSerializer.SerializeToUtf8Bytes( analysis, (JsonTypeInfo<PackageAnalysis>)(indented ? _options : _compact).GetTypeInfo( typeof( PackageAnalysis ) ) );
    }

    /// <summary>
    /// Reads an analysis.
    /// </summary>
    /// <param name="utf8Json">The utf-8 JSON.</param>
    /// <returns>The analysis.</returns>
    public static PackageAnalysis Read( ReadOnlySpan<byte> utf8Json )
    {
        return JsonSerializer.Deserialize( utf8Json, (JsonTypeInfo<PackageAnalysis>)_options.GetTypeInfo( typeof( PackageAnalysis ) ) )
               ?? throw new InvalidDataException( "Null package analysis." );
    }
}

[JsonSourceGenerationOptions( UseStringEnumConverter = true,
                              DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull )]
[JsonSerializable( typeof( PackageAnalysis ) )]
sealed partial class CatalogJsonContext : JsonSerializerContext
{
}
