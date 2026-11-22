using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Configurator.Catalog;

/// <summary>
/// A command of a catalog: the family of its command interfaces, computed from their declarations with the rules of
/// the Poco engine and of the Cris engine.
/// <list type="bullet">
///     <item>
///     The primary interface of a (non abstract) command interface is the single primary interface of its non abstract
///     command bases, or itself when it has none. Two different ones are a conflict: the interface is not in any family.
///     </item>
///     <item>The family is the primary interface and every interface whose primary interface it is.</item>
///     <item>Its name is the <c>[ExternalName]</c> of the primary interface, its C# name otherwise.</item>
///     <item>
///     Its exposure is the most restrictive one declared by the interfaces of the family and by all their bases (the
///     abstract ones included): "Private" wins over "Internal", which wins over "Public".
///     </item>
/// </list>
/// Only the interfaces of the catalog are considered: a base that is not in the catalog (<c>ICommand&lt;TResult&gt;</c>)
/// is ignored.
/// </summary>
public sealed class CommandFamily
{
    internal CommandFamily( string name,
                            (PackageAnalysis Package, TypeAnalysis Type) primary,
                            IReadOnlyList<(PackageAnalysis Package, TypeAnalysis Type)> interfaces,
                            string exposure )
    {
        Name = name;
        Primary = primary;
        Interfaces = interfaces;
        Exposure = exposure;
    }

    /// <summary>
    /// Gets the name of the command: the <c>[ExternalName]</c> of the primary interface or its C# name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the primary interface and its package.
    /// </summary>
    public (PackageAnalysis Package, TypeAnalysis Type) Primary { get; }

    /// <summary>
    /// Gets the interfaces of the family (the primary one first) and their packages.
    /// </summary>
    public IReadOnlyList<(PackageAnalysis Package, TypeAnalysis Type)> Interfaces { get; }

    /// <summary>
    /// Gets the exposure of the command: "Public", "Internal" or "Private".
    /// </summary>
    public string Exposure { get; }

    /// <summary>
    /// Returns the <see cref="Name"/>.
    /// </summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;

    /// <summary>
    /// Computes the families from the command interfaces of a catalog.
    /// </summary>
    /// <param name="monitor">The monitor that logs the conflicts (as warnings).</param>
    /// <param name="types">The types of the catalog, by full name.</param>
    /// <returns>The families, indexed by the full name of each of their interfaces.</returns>
    internal static Dictionary<string, CommandFamily> Compute( IActivityMonitor monitor,
                                                               IReadOnlyDictionary<string, (PackageAnalysis Package, AssemblyAnalysis Assembly, TypeAnalysis Type)> types )
    {
        // Primary interface of each non abstract command interface (null for a conflict).
        var primaries = new Dictionary<string, string?>( StringComparer.Ordinal );
        foreach( var (fullName, t) in types )
        {
            if( t.Type.Command is { IsAbstract: false } ) GetPrimary( fullName, t.Type );
        }
        var members = primaries.Where( kv => kv.Value != null )
                               .GroupBy( kv => kv.Value!, kv => kv.Key, StringComparer.Ordinal );
        var result = new Dictionary<string, CommandFamily>( StringComparer.Ordinal );
        foreach( var g in members )
        {
            var primary = types[g.Key];
            var interfaces = g.OrderBy( n => n == g.Key ? 0 : 1 )
                              .ThenBy( n => n, StringComparer.Ordinal )
                              .Select( n => (types[n].Package, types[n].Type) )
                              .ToList();
            var family = new CommandFamily( primary.Type.Command!.ExternalName ?? primary.Type.CSharpName,
                                            (primary.Package, primary.Type),
                                            interfaces,
                                            GetExposure( interfaces.Select( i => i.Type ) ) );
            foreach( var i in interfaces ) result.Add( i.Type.FullName, family );
        }
        return result;

        string? GetPrimary( string fullName, TypeAnalysis type )
        {
            if( primaries.TryGetValue( fullName, out var known ) ) return known;
            // Guards against a cycle (impossible for interfaces, but the analyses are data).
            primaries.Add( fullName, null );
            string? single = null;
            bool conflict = false;
            foreach( var b in type.Command!.Bases )
            {
                if( !types.TryGetValue( b, out var bType ) || bType.Type.Command is not { IsAbstract: false } ) continue;
                var p = GetPrimary( b, bType.Type );
                if( p == null )
                {
                    conflict = true;
                }
                else if( single == null )
                {
                    single = p;
                }
                else if( single != p )
                {
                    monitor.Warn( $"Command interface '{type.CSharpName}' extends both the families of '{single}' and '{p}': it is ignored." );
                    conflict = true;
                }
            }
            var result = conflict ? null : single ?? fullName;
            primaries[fullName] = result;
            return result;
        }

        string GetExposure( IEnumerable<TypeAnalysis> interfaces )
        {
            var exposure = "Public";
            var visited = new HashSet<string>( StringComparer.Ordinal );
            var toVisit = new Stack<TypeAnalysis>( interfaces );
            while( toVisit.TryPop( out var t ) )
            {
                if( !visited.Add( t.FullName ) || t.Command == null ) continue;
                if( t.Command.Exposure == "Private" ) return "Private";
                if( t.Command.Exposure == "Internal" ) exposure = "Internal";
                foreach( var b in t.Command.Bases )
                {
                    if( types.TryGetValue( b, out var bType ) ) toVisit.Push( bType.Type );
                }
            }
            return exposure;
        }
    }
}
