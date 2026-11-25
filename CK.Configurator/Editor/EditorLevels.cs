using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CK.Configurator;

/// <summary>
/// The levels of a section, the nearest first: where the run time looks for a key of it, key by key (CK.ConfigurationModel's
/// reader, the options of a feature: the party, then the "Defaults" above it). The central "Defaults:App" is merged under
/// every App (a JSON merge: key by key too).
/// <list type="bullet">
///   <item>"Builders:{b}:App:Local:..." (the local party, its features, their options, its clients): the App's "Local",
///   the central "Defaults:App:Local", the App's "Defaults", the central "Defaults:App:Defaults".</item>
///   <item>"Builders:{b}:App:Defaults:...": the App's "Defaults", the central "Defaults:App:Defaults".</item>
///   <item>"Builders:{b}:App:Aspects:...": the App's "Aspects", the central "Defaults:App:Aspects".</item>
///   <item>"Defaults:App:Local:...": itself, then "Defaults:App:Defaults".</item>
///   <item>Anything else: itself.</item>
/// </list>
/// </summary>
static partial class EditorLevels
{
    [GeneratedRegex( @"^(Builders:[^:]+:App):(Local|Defaults|Aspects)(:.*)?$", RegexOptions.IgnoreCase )]
    private static partial Regex AppSection();

    [GeneratedRegex( @"^Defaults:App:Local(:.*)?$", RegexOptions.IgnoreCase )]
    private static partial Regex DefaultsLocal();

    /// <summary>
    /// Gets the levels of a section (the section itself first).
    /// </summary>
    /// <param name="path">The path of the section.</param>
    /// <returns>The paths of the section in its levels, the nearest first.</returns>
    public static IReadOnlyList<string> Of( string path )
    {
        var m = AppSection().Match( path );
        if( m.Success )
        {
            var app = m.Groups[1].Value;
            var rest = m.Groups[3].Value;
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "local" => [path, $"Defaults:App:Local{rest}", $"{app}:Defaults{rest}", $"Defaults:App:Defaults{rest}"],
                "defaults" => [path, $"Defaults:App:Defaults{rest}"],
                _ => [path, $"Defaults:App:Aspects{rest}"]
            };
        }
        m = DefaultsLocal().Match( path );
        if( m.Success ) return [path, $"Defaults:App:Defaults{m.Groups[1].Value}"];
        return [path];
    }
}
