using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Configurator;

/// <summary>
/// The check of the saved configuration of a solution by its builders (the third layer of the validation): each builder
/// of its "Builders" is built, then run with <c>--check &lt;file&gt;</c> (CK.AppBuilder's <c>CKomposableAppBuilder.CheckArgument</c>:
/// the run stops before the engine). Two builders at a time; a new check cancels the running one.
/// <para>
/// A builder is run only when its CK.AppBuilder has the check (its <c>CheckArgument</c> is in the metadata of the built
/// assembly): an older one would run the whole build (the generated code, a database setup).
/// </para>
/// </summary>
sealed class SolutionCheckRunner
{
    const string _checkArgument = "--check";
    const int _maxParallel = 2;

    readonly object _lock = new();
    CancellationTokenSource? _cts;
    int _generation;
    string _status = "None";
    IReadOnlyList<BuilderCheck> _results = [];

    /// <summary>
    /// The result of the check of a builder.
    /// </summary>
    /// <param name="Builder">The path of the builder in "Builders".</param>
    /// <param name="Entries">The warnings and the errors of its run.</param>
    /// <param name="Failure">Why it couldn't be checked (no result), or null.</param>
    public sealed record BuilderCheck( string Builder, IReadOnlyList<(string Level, string Text)> Entries, string? Failure )
    {
        public bool HasErrors => Failure != null || Entries.Any( e => e.Level is "Error" or "Fatal" );
    }

    /// <summary>
    /// Gets the status ("None", "Running", "Succeeded" or "Failed") and the results of the last check.
    /// </summary>
    public (string Status, IReadOnlyList<BuilderCheck> Results) Get()
    {
        lock( _lock ) return (_status, _results);
    }

    /// <summary>
    /// Forgets the check (a solution is closed or opened).
    /// </summary>
    public void Reset()
    {
        lock( _lock )
        {
            _cts?.Cancel();
            _cts = null;
            ++_generation;
            _status = "None";
            _results = [];
        }
    }

    /// <summary>
    /// Starts the check of the builders of a solution (the running one is canceled).
    /// </summary>
    /// <param name="folder">The folder of the solution.</param>
    /// <param name="builders">The paths of the builders ("Front/Auth3.Front.Builder").</param>
    public void Start( string folder, IReadOnlyList<string> builders )
    {
        CancellationTokenSource cts;
        int generation;
        lock( _lock )
        {
            _cts?.Cancel();
            _cts = cts = new CancellationTokenSource();
            generation = ++_generation;
            _status = builders.Count == 0 ? "Succeeded" : "Running";
            _results = [];
        }
        if( builders.Count > 0 ) _ = RunAsync( folder, builders, generation, cts.Token );
    }

    async Task RunAsync( string folder, IReadOnlyList<string> builders, int generation, CancellationToken cancel )
    {
        var monitor = new ActivityMonitor( $"Checking the configuration of '{folder}'." );
        try
        {
            using var slots = new SemaphoreSlim( _maxParallel );
            var checks = await Task.WhenAll( builders.Select( async b =>
            {
                await slots.WaitAsync( cancel ).ConfigureAwait( false );
                try
                {
                    return await CheckBuilderAsync( monitor, folder, b, cancel ).ConfigureAwait( false );
                }
                finally
                {
                    slots.Release();
                }
            } ) ).ConfigureAwait( false );
            lock( _lock )
            {
                if( generation != _generation ) return;
                _results = checks;
                _status = checks.Any( c => c.HasErrors ) ? "Failed" : "Succeeded";
            }
        }
        catch( OperationCanceledException )
        {
            // A new check (or a close) replaced this one.
        }
        catch( Exception ex )
        {
            monitor.Error( ex );
            lock( _lock )
            {
                if( generation != _generation ) return;
                _results = [new BuilderCheck( "", [], $"The check failed: {ex.Message}" )];
                _status = "Failed";
            }
        }
        finally
        {
            monitor.MonitorEnd();
        }
    }

    static async Task<BuilderCheck> CheckBuilderAsync( IActivityMonitor monitor, string folder, string builder, CancellationToken cancel )
    {
        var projectFolder = Path.GetFullPath( Path.Combine( folder, builder ) );
        var project = Path.Combine( projectFolder, Path.GetFileName( builder.TrimEnd( '/' ) ) + ".csproj" );
        if( !File.Exists( project ) )
        {
            return new BuilderCheck( builder, [], $"The project '{project}' doesn't exist." );
        }
        var output = new List<string>();
        if( await RunAsync( monitor, ["build", project, "-c", "Debug", "--nologo", "-v", "q"], projectFolder, output, cancel ).ConfigureAwait( false ) != 0 )
        {
            return new BuilderCheck( builder, [], $"The build of the builder failed: {Summarize( output )}" );
        }
        var appBuilder = Directory.EnumerateFiles( Path.Combine( projectFolder, "bin", "Debug" ), "CK.AppBuilder.dll", SearchOption.AllDirectories )
                                  .OrderByDescending( File.GetLastWriteTimeUtc )
                                  .FirstOrDefault();
        if( appBuilder == null || !HasCheck( appBuilder ) )
        {
            return new BuilderCheck( builder, [], "Its CK.AppBuilder can't check a configuration (it has no --check): a newer version is needed. The builder is not run (it would run the whole build)." );
        }
        var resultFile = Path.Combine( Path.GetTempPath(), $"ck-configurator-check-{Guid.NewGuid():N}.json" );
        try
        {
            output.Clear();
            await RunAsync( monitor, ["run", "--no-build", "-c", "Debug", "--project", project, "--", _checkArgument, resultFile], projectFolder, output, cancel ).ConfigureAwait( false );
            if( !File.Exists( resultFile ) )
            {
                return new BuilderCheck( builder, [], $"The builder wrote no result: {Summarize( output )}" );
            }
            using var d = JsonDocument.Parse( File.ReadAllText( resultFile ) );
            var entries = d.RootElement.GetProperty( "Entries" ).EnumerateArray()
                           .Select( e => (e.GetProperty( "Level" ).GetString() ?? "Error", e.GetProperty( "Text" ).GetString() ?? "") )
                           .ToList();
            return new BuilderCheck( builder, entries, null );
        }
        finally
        {
            try { File.Delete( resultFile ); } catch( IOException ) { }
        }
    }

    // The errors of MSBuild, or the end of the output.
    static string Summarize( List<string> output )
    {
        var errors = output.Where( l => l.Contains( ": error " ) ).Distinct().Take( 10 ).ToList();
        return string.Join( Environment.NewLine, errors.Count > 0 ? errors : output.TakeLast( 15 ) );
    }

    // Whether the CK.AppBuilder assembly declares CKomposableAppBuilder.CheckArgument (read from its metadata: the
    // assembly is not loaded).
    internal static bool HasCheck( string assemblyPath )
    {
        try
        {
            using var stream = File.OpenRead( assemblyPath );
            using var pe = new PEReader( stream );
            var md = pe.GetMetadataReader();
            foreach( var h in md.TypeDefinitions )
            {
                var t = md.GetTypeDefinition( h );
                if( !md.StringComparer.Equals( t.Name, "CKomposableAppBuilder" ) || !md.StringComparer.Equals( t.Namespace, "CK.Setup" ) ) continue;
                return t.GetFields().Any( f => md.StringComparer.Equals( md.GetFieldDefinition( f ).Name, "CheckArgument" ) );
            }
            return false;
        }
        catch( Exception )
        {
            return false;
        }
    }

    static async Task<int> RunAsync( IActivityMonitor monitor, IEnumerable<string> arguments, string workingDirectory, List<string> output, CancellationToken cancel )
    {
        var info = new ProcessStartInfo( "dotnet" )
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach( var a in arguments ) info.ArgumentList.Add( a );
        // The builder's own console: no color codes in the captured output.
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        using var p = new Process { StartInfo = info };
        void OnLine( object sender, DataReceivedEventArgs e )
        {
            if( e.Data == null ) return;
            lock( output ) output.Add( e.Data );
        }
        p.OutputDataReceived += OnLine;
        p.ErrorDataReceived += OnLine;
        monitor.Info( $"dotnet {string.Join( ' ', info.ArgumentList )}" );
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync( cancel ).ConfigureAwait( false );
        }
        catch( OperationCanceledException )
        {
            try { p.Kill( entireProcessTree: true ); } catch( InvalidOperationException ) { }
            throw;
        }
        return p.ExitCode;
    }
}
