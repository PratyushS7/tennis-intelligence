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
                var expectedKey = connectorOptions.Value.ApiKey;
                if (string.IsNullOrWhiteSpace(expectedKey))
                {
                    return Results.Problem(
                        title: "Connector synchronization is disabled.",
                        detail: "Configure Connector:ApiKey before accepting device synchronization.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!request.Headers.TryGetValue(ConnectorOptions.ApiKeyHeader, out var suppliedKey)
                    || !KeysMatch(expectedKey, suppliedKey.ToString()))
                {
                    return Results.Unauthorized();
                }

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
                var expectedKey = connectorOptions.Value.ApiKey;
                if (string.IsNullOrWhiteSpace(expectedKey))
                {
                    return Results.Problem(
                        title: "Connector synchronization is disabled.",
                        detail: "Configure Connector:ApiKey before accepting device synchronization.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!request.Headers.TryGetValue(ConnectorOptions.ApiKeyHeader, out var suppliedKey)
                    || !KeysMatch(expectedKey, suppliedKey.ToString()))
                {
                    return Results.Unauthorized();
                }

                var report = await trainingLoad.GetReportAsync(cancellationToken);
                var lines = TrainingLoadNarrative.Describe(report);

                // The server renders the sentences so the wording can improve without a new APK.
                return Results.Ok(new ConnectorTrainingLoadResponse(lines.Count > 0, lines));
            })
            .WithName("GetConnectorTrainingLoad");

        return endpoints;
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
