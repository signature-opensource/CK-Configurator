using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CK.Configurator.Catalog;

/// <summary>
/// A request to a worker server (see <see cref="CatalogWorkerServer"/>): one JSON line on its standard input. Exactly
/// one request member is set.
/// </summary>
public sealed class WorkerRequest
{
    /// <summary>
    /// Gets the identifier of the request, echoed by its <see cref="WorkerResponse"/>.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the closure request.
    /// </summary>
    public ClosureRequest? Closure { get; init; }
}

/// <summary>
/// The response of a worker server: one JSON line on its standard output.
/// </summary>
public sealed class WorkerResponse
{
    /// <summary>
    /// Gets the identifier of the request.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the errors (the error and fatal logs of the request). A response with errors has no result.
    /// </summary>
    public IReadOnlyList<string> Errors { get => field ?? []; init; }

    /// <summary>
    /// Gets the result of a <see cref="WorkerRequest.Closure"/>.
    /// </summary>
    public ClosureResult? Closure { get; init; }
}

/// <summary>
/// Computes the packages that a ResourceSpace keeps from candidates and roots (the rule of <c>ResPackageClosure</c>
/// in CK.ResourceSpace, run on the types of the worker): the roots are kept, the required references of a kept package
/// are kept (forward only), the optional ones never keep a package, a reference to a single abstraction keeps its
/// implementation.
/// </summary>
public sealed class ClosureRequest
{
    /// <summary>
    /// Gets the full names of the root packages (registered as required).
    /// </summary>
    public IReadOnlyList<string> Roots { get => field ?? []; init; }

    /// <summary>
    /// Gets the full names of the packages of the assemblies that provide the candidates: every resource group of these
    /// packages is a candidate (registered as optional, the roots as required).
    /// </summary>
    public IReadOnlyList<string> Packages { get => field ?? []; init; }
}

/// <summary>
/// The result of a <see cref="ClosureRequest"/>.
/// </summary>
public sealed class ClosureResult
{
    /// <summary>
    /// Gets the full names of the kept packages, ordered.
    /// </summary>
    public IReadOnlyList<string> Kept { get => field ?? []; init; }
}

/// <summary>
/// The JSON of the worker protocol: compact (one line per message).
/// </summary>
public static class WorkerProtocol
{
    static readonly JsonSerializerOptions _options = new JsonSerializerOptions( WorkerJsonContext.Default.Options )
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = WorkerJsonContext.Default
    };

    /// <summary>
    /// Writes a request on one line.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The JSON line.</returns>
    public static string Write( WorkerRequest request ) => JsonSerializer.Serialize( request, Info<WorkerRequest>() );

    /// <summary>
    /// Writes a response on one line.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON line.</returns>
    public static string Write( WorkerResponse response ) => JsonSerializer.Serialize( response, Info<WorkerResponse>() );

    /// <summary>
    /// Reads a request.
    /// </summary>
    /// <param name="line">The JSON line.</param>
    /// <returns>The request.</returns>
    public static WorkerRequest ReadRequest( string line ) => JsonSerializer.Deserialize( line, Info<WorkerRequest>() )
                                                              ?? throw new JsonException( "Null request." );

    /// <summary>
    /// Reads a response.
    /// </summary>
    /// <param name="line">The JSON line.</param>
    /// <returns>The response.</returns>
    public static WorkerResponse ReadResponse( string line ) => JsonSerializer.Deserialize( line, Info<WorkerResponse>() )
                                                                ?? throw new JsonException( "Null response." );

    static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)_options.GetTypeInfo( typeof( T ) );
}

[JsonSourceGenerationOptions( DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull )]
[JsonSerializable( typeof( WorkerRequest ) )]
[JsonSerializable( typeof( WorkerResponse ) )]
sealed partial class WorkerJsonContext : JsonSerializerContext
{
}
