using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static CK.Testing.MonitorTestHelper;

namespace CK.Configurator.Tests;

/// <summary>
/// The packages of the test profiles: real packages that declare configurable types, configuration declarations, client
/// facts and commands. Their versions are the ones that the test project references (ckli updates them with every
/// build): the analyzer is built with the same line, the profile is always analyzable. This file is shared by the test
/// projects of the repository.
/// </summary>
static class FixturePackages
{
    /// <summary>
    /// The package identifiers (each one is the name of its assembly).
    /// </summary>
    public static readonly IReadOnlyList<string> Ids = ["CK.AppIdentity.PartyClient",
                                                        "CK.AppIdentity.TransportLayer",
                                                        "CK.AppIdentity.HttpEndpoint",
                                                        "CK.IO.DynamicConfiguration",
                                                        "CK.Globalization.StObj"];

    /// <summary>
    /// Gets the version of a package: the informational version ("Version/Sha/Date") of its assembly.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>The package version.</returns>
    public static string GetVersion( string packageId )
    {
        var info = Assembly.Load( new AssemblyName( packageId ) ).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? throw new InvalidOperationException( $"No informational version for '{packageId}'." );
        return info.Split( '/' )[0];
    }

    /// <summary>
    /// Gets "PackageId@Version".
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>The package instance.</returns>
    public static string Pin( string packageId ) => $"{packageId}@{GetVersion( packageId )}";

    /// <summary>
    /// Gets the local feed of the Stack ("$Local/NuGet" of the ".PublicStack" next to the repositories of the World)
    /// when it exists: the packages built by ckli and not published yet are there.
    /// </summary>
    public static string? LocalFeed
    {
        get
        {
            var path = Path.GetFullPath( Path.Combine( TestHelper.SolutionFolder, "..", ".PublicStack", "$Local", "NuGet" ) );
            return Directory.Exists( path ) ? path.Replace( '\\', '/' ) : null;
        }
    }
}
