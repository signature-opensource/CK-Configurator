using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace CK.Configurator.Catalog;

/// <summary>
/// The worker of a source: a generated project that references the packages of the source and the analyzer
/// (CK.Configurator.Catalog.Worker), restored, built and run. Its folder holds exactly one version of each package:
/// the analyzer loads their types (the TypeCache loads the assemblies of its base directory) and runs their code.
/// <para>
/// One source, one worker process: another source is another worker, nothing is unloaded.
/// </para>
/// </summary>
public static class CatalogWorker
{
    /// <summary>
    /// The arguments of the worker program.
    /// </summary>
    public static class Arguments
    {
        /// <summary>
        /// The path of the "project.assets.json" of the worker project: it maps the packages to their assemblies.
        /// </summary>
        public const string Assets = "--assets";

        /// <summary>
        /// The root of the <see cref="CatalogCache"/>.
        /// </summary>
        public const string Cache = "--cache";

        /// <summary>
        /// The path of a text file: one "PackageId@Version" per line, the packages to analyze.
        /// </summary>
        public const string Packages = "--packages";

        /// <summary>
        /// Runs the worker as a server (see <see cref="CatalogWorkerServer"/>): it answers the <see cref="WorkerRequest"/>
        /// lines of its standard input until it is closed.
        /// </summary>
        public const string Serve = "--serve";
    }

    /// <summary>
    /// Analyzes the packages of a source that are not in the cache yet: generates the worker project in
    /// <paramref name="workerFolder"/>, builds it and runs it.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="source">The source.</param>
    /// <param name="cache">The cache of the analyses.</param>
    /// <param name="workerFolder">The folder of the worker project (one per source).</param>
    /// <param name="analyzer">How the worker project references the analyzer.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public static async Task<bool> AnalyzeAsync( IActivityMonitor monitor,
                                                 CatalogSource source,
                                                 CatalogCache cache,
                                                 string workerFolder,
                                                 AnalyzerReference analyzer,
                                                 CancellationToken cancel = default )
    {
        var packages = source.CatalogPackages.ToList();
        var missing = packages.Where( p => !cache.Contains( p.PackageId, p.Version.ToString() ) ).ToList();
        if( missing.Count == 0 )
        {
            monitor.Info( $"The {packages.Count} packages of '{source.Profile}' are already analyzed." );
            return true;
        }
        using( monitor.OpenInfo( $"Analyzing {missing.Count} packages (out of {packages.Count}) of '{source.Profile}' in '{workerFolder}'." ) )
        {
            workerFolder = Path.GetFullPath( workerFolder );
            if( !await BuildAsync( monitor, source, workerFolder, analyzer, cancel ).ConfigureAwait( false ) ) return false;
            var packagesFile = Path.Combine( workerFolder, "packages.txt" );
            File.WriteAllLines( packagesFile, missing.Select( p => $"{p.PackageId}@{p.Version}" ) );
            if( !await RunAsync( monitor,
                                 workerFolder,
                                 GetExecutablePath( workerFolder ),
                                 [Arguments.Assets, GetAssetsPath( workerFolder ), Arguments.Cache, cache.Root, Arguments.Packages, packagesFile],
                                 cancel ).ConfigureAwait( false ) )
            {
                monitor.Error( "The worker failed." );
                return false;
            }
            var stillMissing = missing.Where( p => !cache.Contains( p.PackageId, p.Version.ToString() ) ).ToList();
            if( stillMissing.Count > 0 )
            {
                monitor.Error( $"The worker didn't analyze {stillMissing.Count} packages: '{stillMissing.Select( p => p.ToString() ).Concatenate( "', '" )}'." );
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Starts the worker of a source as a server: the worker project is generated and built (when needed), the worker
    /// process loads the packages once and answers the requests until the returned server is disposed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="source">The source.</param>
    /// <param name="workerFolder">The folder of the worker project (one per source).</param>
    /// <param name="analyzer">How the worker project references the analyzer.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The server or null on error.</returns>
    public static async Task<CatalogWorkerServer?> StartServerAsync( IActivityMonitor monitor,
                                                                    CatalogSource source,
                                                                    string workerFolder,
                                                                    AnalyzerReference analyzer,
                                                                    CancellationToken cancel = default )
    {
        workerFolder = Path.GetFullPath( workerFolder );
        if( !await BuildAsync( monitor, source, workerFolder, analyzer, cancel ).ConfigureAwait( false ) ) return null;
        return CatalogWorkerServer.Start( monitor,
                                          workerFolder,
                                          GetExecutablePath( workerFolder ),
                                          [Arguments.Serve, Arguments.Assets, GetAssetsPath( workerFolder )] );
    }

    static async Task<bool> BuildAsync( IActivityMonitor monitor,
                                        CatalogSource source,
                                        string workerFolder,
                                        AnalyzerReference analyzer,
                                        CancellationToken cancel )
    {
        Directory.CreateDirectory( workerFolder );
        WriteProject( workerFolder, source, source.CatalogPackages.Select( p => (p.PackageId, p.Version.ToString()) ), analyzer );
        if( !await RunAsync( monitor, workerFolder, "dotnet", ["build", "-c", "Release", "-nologo", "-v:q"], cancel ).ConfigureAwait( false ) )
        {
            monitor.Error( "The worker project failed to build." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets whether a package is the analyzer itself. The worker brings its own code: a profile that contains the
    /// Configurator must not pin another version of it (the worker would run the analysis of that version, in its
    /// format), so these packages are not referenced and the worker analyzes its own assemblies for them.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>True for the analyzer packages.</returns>
    public static bool IsAnalyzerPackage( string packageId ) => packageId == "CK.Configurator.Catalog"
                                                                || packageId == "CK.Configurator.Catalog.Worker";

    static string GetExecutablePath( string workerFolder ) => Path.Combine( workerFolder,
                                                                            "bin",
                                                                            "Release",
                                                                            "net10.0",
                                                                            OperatingSystem.IsWindows() ? "CatalogWorker.exe" : "CatalogWorker" );

    static string GetAssetsPath( string workerFolder ) => Path.Combine( workerFolder, "obj", "project.assets.json" );

    /// <summary>
    /// Writes the worker project: its csproj, its program, its nuget.config (the feeds of the source) and empty
    /// Directory.Build.props/.targets (nothing above the folder is imported).
    /// </summary>
    /// <param name="folder">The worker folder.</param>
    /// <param name="source">The source (for its feeds).</param>
    /// <param name="packages">The packages to reference.</param>
    /// <param name="analyzer">How the analyzer is referenced.</param>
    public static void WriteProject( string folder,
                                     CatalogSource source,
                                     IEnumerable<(string Id, string Version)> packages,
                                     AnalyzerReference analyzer )
    {
        var p = new StringBuilder();
        p.AppendLine( """
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- Generated by CK.Configurator.Catalog: the worker of a catalog source. -->
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>CatalogWorker</AssemblyName>
                <Nullable>enable</Nullable>
                <ImplicitUsings>disable</ImplicitUsings>
                <!-- The packages of a profile are coherent, not necessarily without downgrade warnings. -->
                <WarningsNotAsErrors>NU1605;NU1608</WarningsNotAsErrors>
                <NoWarn>$(NoWarn);NU1603;NU1605;NU1608;NU1701</NoWarn>
                <!-- The Xml documentation files of the packages are next to their assemblies: the analyzer reads them. -->
                <CopyDocumentationFilesFromPackages>true</CopyDocumentationFilesFromPackages>
              </PropertyGroup>
              <ItemGroup>
            """ );
        foreach( var (id, version) in packages )
        {
            if( IsAnalyzerPackage( id ) ) continue;
            // Only the runtime assemblies matter: the build files of a package (an App host, an App builder...) would
            // act on the worker project.
            p.AppendLine( $"""    <PackageReference Include="{SecurityElement.Escape( id )}" Version="[{SecurityElement.Escape( version )}]" ExcludeAssets="build;buildTransitive;analyzers" />""" );
        }
        p.AppendLine( analyzer.ProjectPath != null
                        ? $"""    <ProjectReference Include="{SecurityElement.Escape( analyzer.ProjectPath )}" />"""
                        : $"""    <PackageReference Include="CK.Configurator.Catalog.Worker" Version="{SecurityElement.Escape( analyzer.PackageVersion )}" />""" );
        p.AppendLine( """
              </ItemGroup>
            </Project>
            """ );
        WriteIfChanged( Path.Combine( folder, "CatalogWorker.csproj" ), p.ToString() );
        WriteIfChanged( Path.Combine( folder, "Program.cs" ),
                        """
                        // Generated by CK.Configurator.Catalog.
                        return CK.Configurator.Catalog.Worker.WorkerProgram.Run( args );

                        """ );
        WriteIfChanged( Path.Combine( folder, "Directory.Build.props" ), "<Project />" );
        WriteIfChanged( Path.Combine( folder, "Directory.Build.targets" ), "<Project />" );

        var n = new StringBuilder();
        n.AppendLine( """
            <configuration>
              <packageSources>
                <clear />
            """ );
        foreach( var f in source.Feeds )
        {
            n.AppendLine( $"""    <add key="{SecurityElement.Escape( f.Name )}" value="{SecurityElement.Escape( f.Url )}" />""" );
        }
        n.AppendLine( "  </packageSources>" );
        // NuGet merges the nuget.config files of the parent folders (and the user's one): a package source mapping
        // defined there would ignore the feeds of the source. The worker is isolated from them.
        n.AppendLine( """
              <packageSourceMapping>
                <clear />
              </packageSourceMapping>
            """ );
        // The public read credentials are not secrets (a GitHub public feed requires an authentication).
        var publicRead = source.Feeds.Where( f => f.PublicReadUserName != null ).ToList();
        if( publicRead.Count > 0 )
        {
            n.AppendLine( "  <packageSourceCredentials>" );
            foreach( var f in publicRead )
            {
                n.AppendLine( $"""    <{XmlConvert.EncodeLocalName( f.Name )}>""" );
                n.AppendLine( $"""      <add key="Username" value="{SecurityElement.Escape( f.PublicReadUserName )}" />""" );
                n.AppendLine( $"""      <add key="ClearTextPassword" value="{SecurityElement.Escape( f.PublicReadPassword )}" />""" );
                n.AppendLine( $"""    </{XmlConvert.EncodeLocalName( f.Name )}>""" );
            }
            n.AppendLine( "  </packageSourceCredentials>" );
        }
        n.AppendLine( "</configuration>" );
        WriteIfChanged( Path.Combine( folder, "nuget.config" ), n.ToString() );

        static void WriteIfChanged( string path, string content )
        {
            if( !File.Exists( path ) || File.ReadAllText( path ) != content ) File.WriteAllText( path, content );
        }
    }

    static async Task<bool> RunAsync( IActivityMonitor monitor, string workingDirectory, string fileName, string[] args, CancellationToken cancel )
    {
        using( monitor.OpenInfo( $"{fileName} {args.Concatenate( " " )}" ) )
        {
            var info = new ProcessStartInfo( fileName, args )
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // The worker project must not inherit the MSBuild environment of a calling build or test.
            info.Environment.Remove( "MSBuildSDKsPath" );
            info.Environment.Remove( "MSBuildExtensionsPath" );
            info.Environment.Remove( "MSBUILD_EXE_PATH" );
            using var process = new Process { StartInfo = info };
            var output = new StringBuilder();
            var gate = new Lock();
            process.OutputDataReceived += ( _, e ) => { if( e.Data != null ) lock( gate ) output.AppendLine( e.Data ); };
            process.ErrorDataReceived += ( _, e ) => { if( e.Data != null ) lock( gate ) output.AppendLine( e.Data ); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync( cancel ).ConfigureAwait( false );
            process.WaitForExit();
            var text = output.ToString();
            if( process.ExitCode != 0 )
            {
                monitor.Error( $"Exit code {process.ExitCode}:{Environment.NewLine}{text}" );
                return false;
            }
            if( text.Length > 0 ) monitor.Trace( text );
            return true;
        }
    }
}

/// <summary>
/// How the worker project references the analyzer (CK.Configurator.Catalog.Worker): by its package (the regular
/// case) or by its project (the tests and the development of the analyzer).
/// </summary>
public sealed class AnalyzerReference
{
    AnalyzerReference( string? packageVersion, string? projectPath )
    {
        PackageVersion = packageVersion;
        ProjectPath = projectPath;
    }

    /// <summary>
    /// References the CK.Configurator.Catalog.Worker package.
    /// </summary>
    /// <param name="version">The package version.</param>
    /// <returns>The reference.</returns>
    public static AnalyzerReference FromPackage( string version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( version );
        return new AnalyzerReference( version, null );
    }

    /// <summary>
    /// References the CK.Configurator.Catalog.Worker project.
    /// </summary>
    /// <param name="projectPath">The path of the csproj.</param>
    /// <returns>The reference.</returns>
    public static AnalyzerReference FromProject( string projectPath )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( projectPath );
        return new AnalyzerReference( null, Path.GetFullPath( projectPath ) );
    }

    /// <summary>
    /// Gets the package version (null when <see cref="ProjectPath"/> is set).
    /// </summary>
    public string? PackageVersion { get; }

    /// <summary>
    /// Gets the project path (null when <see cref="PackageVersion"/> is set).
    /// </summary>
    public string? ProjectPath { get; }
}
