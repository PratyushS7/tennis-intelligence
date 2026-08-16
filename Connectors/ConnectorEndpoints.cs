using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TennisIntelligence.Services;

namespace TennisIntelligence.Connectors;

public static class ConnectorEndpoints
{
    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/connectors/workouts",
            async (
                HttpRequest request,
                WearableImportPackage package,
                WearableImportService importService,
                IOptions<ConnectorOptions> connectorOptions,
                CancellationToken cancellationToken) =>
            {
                if (Reject(request, connectorOptions.Value) is IResult refusal) return refusal;

                try
                {
                    var result = await importService.ImportPackageAsync(
                        package,
                        $"connector-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                        cancellationToken);

                    return Results.Ok(new ConnectorSyncResponse(
                        result.Batch.Id,
                        result.Batch.Status,
                        result.Batch.InsertedRecords,
                        result.Batch.UpdatedRecords,
                        result.Batch.UnchangedRecords,
                        result.Batch.RejectedRecords,
                        result.Errors));
                }
                catch (WearableImportValidationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("SyncConnectorWorkouts")
            .DisableAntiforgery();

        endpoints.MapGet(
            "/api/connectors/training-load",
            async (
                HttpRequest request,
                TrainingLoadService trainingLoad,
                IOptions<ConnectorOptions> connectorOptions,
                CancellationToken cancellationToken) =>
            {
                if (Reject(request, connectorOptions.Value) is IResult refusal) return refusal;

                var report = await trainingLoad.GetReportAsync(cancellationToken);
                var lines = TrainingLoadNarrative.Describe(report);

                // The server renders the sentences so the wording can improve without a new APK.
                return Results.Ok(new ConnectorTrainingLoadResponse(lines.Count > 0, lines));
            })
            .WithName("GetConnectorTrainingLoad");

        endpoints.MapGet(
            "/api/connectors/pending-log",
            async (
                HttpRequest request,
                SessionLogService sessionLog,
                IOptions<ConnectorOptions> connectorOptions,
                CancellationToken cancellationToken) =>
            {
                if (Reject(request, connectorOptions.Value) is IResult refusal) return refusal;

                var pending = await sessionLog.GetPendingAsync(DateTimeOffset.UtcNow, cancellationToken);
                return Results.Ok(new ConnectorPendingLogResponse(pending is not null, pending));
            })
            .WithName("GetConnectorPendingLog");

        endpoints.MapPost(
            "/api/connectors/session-log",
            async (
                HttpRequest request,
                SessionLogRequest body,
                SessionLogService sessionLog,
                IOptions<ConnectorOptions> connectorOptions,
                CancellationToken cancellationToken) =>
            {
                if (Reject(request, connectorOptions.Value) is IResult refusal) return refusal;

                var result = await sessionLog.LogAsync(body, DateTimeOffset.UtcNow, cancellationToken);
                return result.Outcome switch
                {
                    SessionLogOutcome.Created => Results.Ok(new { sessionId = result.SessionId }),
                    SessionLogOutcome.AlreadyLogged => Results.Conflict(new { error = "That session has already been logged." }),
                    SessionLogOutcome.InvalidRating => Results.BadRequest(new { error = "Rating must be between 1 and 5." }),
                    SessionLogOutcome.WorkoutNotFound => Results.NotFound(new { error = "No tennis workout with that id." }),
                    _ => Results.Problem("Unhandled session log outcome.")
                };
            })
            .WithName("PostConnectorSessionLog")
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>
    /// Rejects a request that is not from the paired device. Returns null when the caller may
    /// proceed, so every connector endpoint refuses in exactly the same way.
    /// </summary>
    private static IResult? Reject(HttpRequest request, ConnectorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return Results.Problem(
                title: "Connector synchronization is disabled.",
                detail: "Configure Connector:ApiKey before accepting device synchronization.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!request.Headers.TryGetValue(ConnectorOptions.ApiKeyHeader, out var suppliedKey)
            || !KeysMatch(options.ApiKey, suppliedKey.ToString()))
        {
            return Results.Unauthorized();
        }

        return null;
    }

    private static bool KeysMatch(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);

        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}

public sealed class ConnectorOptions
{
    public const string SectionName = "Connector";
    public const string ApiKeyHeader = "X-Connector-Key";

    public string? ApiKey { get; set; }
    public string? PairingServerUrl { get; set; }
}

public sealed record ConnectorSyncResponse(
    int ImportBatchId,
    string Status,
    int Inserted,
    int Updated,
    int Unchanged,
    int Rejected,
    IReadOnlyList<string> Errors);

/// <summary>The training picture as ready-to-display sentences, for the phone to show after a sync.</summary>
public sealed record ConnectorTrainingLoadResponse(
    bool HasData,
    IReadOnlyList<string> Lines);

/// <summary>A session the watch recorded that has not been described yet, if there is one.</summary>
public sealed record ConnectorPendingLogResponse(
    bool HasPending,
    PendingSessionLog? Pending);
