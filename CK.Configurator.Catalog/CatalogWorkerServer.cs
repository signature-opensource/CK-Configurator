using CK.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Configurator.Catalog;

/// <summary>
/// A worker running as a server (see <see cref="CatalogWorker.StartServerAsync"/>): the packages of its source are
/// loaded once, the requests run the rules on their real types. The requests are sent one at a time (a request is a
/// line on the standard input of the worker, its response a line on its standard output); the logs of the worker are
/// its standard error, relayed to the monitor of the request that is running.
/// <para>
/// This channel is internal to the configurator's backend: it is never exposed.
/// </para>
/// </summary>
public sealed class CatalogWorkerServer : IAsyncDisposable
{
    readonly Process _process;
    readonly SemaphoreSlim _gate;
    readonly ConcurrentQueue<string> _logs;
    int _nextId;

    CatalogWorkerServer( Process process )
    {
        _process = process;
        _gate = new SemaphoreSlim( 1, 1 );
        _logs = new ConcurrentQueue<string>();
    }

    internal static CatalogWorkerServer? Start( IActivityMonitor monitor, string workingDirectory, string fileName, string[] args )
    {
        var info = new ProcessStartInfo( fileName, args )
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to start the worker '{fileName}'.", ex );
            process.Dispose();
            return null;
        }
        var server = new CatalogWorkerServer( process );
        process.ErrorDataReceived += ( _, e ) => { if( e.Data != null ) server._logs.Enqueue( e.Data ); };
        process.BeginErrorReadLine();
        monitor.Info( $"Worker server started (process {process.Id})." );
        return server;
    }

    /// <summary>
    /// Gets whether the worker process is running.
    /// </summary>
    public bool IsRunning => !_process.HasExited;

    /// <summary>
    /// Sends a request and waits for its response.
    /// </summary>
    /// <param name="monitor">The monitor to use (it receives the logs of the worker).</param>
    /// <param name="request">The request (its <see cref="WorkerRequest.Id"/> is set by this method).</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The response or null if the worker failed (it has exited or answered something unreadable).</returns>
    public async Task<WorkerResponse?> SendAsync( IActivityMonitor monitor, WorkerRequest request, CancellationToken cancel = default )
    {
        await _gate.WaitAsync( cancel ).ConfigureAwait( false );
        try
        {
            if( _process.HasExited )
            {
                monitor.Error( $"The worker has exited (code {_process.ExitCode})." );
                RelayLogs( monitor );
                return null;
            }
            var id = ++_nextId;
            var line = WorkerProtocol.Write( new WorkerRequest { Id = id, Closure = request.Closure } );
            await _process.StandardInput.WriteLineAsync( line.AsMemory(), cancel ).ConfigureAwait( false );
            await _process.StandardInput.FlushAsync( cancel ).ConfigureAwait( false );
            var answer = await _process.StandardOutput.ReadLineAsync( cancel ).ConfigureAwait( false );
            RelayLogs( monitor );
            if( answer == null )
            {
                monitor.Error( "The worker closed its output." );
                return null;
            }
            var response = WorkerProtocol.ReadResponse( answer );
            if( response.Id != id )
            {
                monitor.Error( $"The worker answered request {response.Id} instead of {id}." );
                return null;
            }
            foreach( var e in response.Errors ) monitor.Error( e );
            return response;
        }
        catch( Exception ex ) when( ex is not OperationCanceledException )
        {
            monitor.Error( "The worker failed.", ex );
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Computes the packages that a ResourceSpace keeps (see <see cref="ClosureRequest"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="roots">The full names of the root packages.</param>
    /// <param name="packages">The NuGet packages whose resource groups are the candidates.</param>
    /// <param name="cancel">Optional cancellation token.</param>
    /// <returns>The full names of the kept packages or null on error.</returns>
    public async Task<IReadOnlyList<string>?> GetClosureAsync( IActivityMonitor monitor,
                                                              IReadOnlyList<string> roots,
                                                              IReadOnlyList<string> packages,
                                                              CancellationToken cancel = default )
    {
        var r = await SendAsync( monitor, new WorkerRequest { Closure = new ClosureRequest { Roots = roots, Packages = packages } }, cancel )
                        .ConfigureAwait( false );
        return r == null || r.Errors.Count > 0 ? null : r.Closure?.Kept ?? [];
    }

    void RelayLogs( IActivityMonitor monitor )
    {
        while( _logs.TryDequeue( out var l ) ) monitor.Trace( l );
    }

    /// <summary>
    /// Closes the standard input of the worker (it exits) and waits for it (5 seconds before killing it).
    /// </summary>
    /// <returns>The awaitable.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if( !_process.HasExited )
            {
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
                try
                {
                    await _process.WaitForExitAsync( timeout.Token ).ConfigureAwait( false );
                }
                catch( OperationCanceledException )
                {
                    _process.Kill( entireProcessTree: true );
                }
            }
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }
    }
}
