using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TennisIntelligence.Data;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

public sealed class WearableImportService
{
    public const int SupportedSchemaVersion = 2;
    public const long MaximumFileSizeBytes = 10 * 1024 * 1024;
    public const int MaximumRecordsPerImport = 5_000;
    private const int MaximumConcurrencyAttempts = 3;
    private static readonly TimeSpan WorkoutStartTolerance = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TennisDbContext _db;

    public WearableImportService(TennisDbContext db)
    {
        _db = db;
    }

    public async Task<WearableImportResult> ImportAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken)
    {
        WearableImportPackage? package;
        try
        {
            package = await JsonSerializer.DeserializeAsync<WearableImportPackage>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new WearableImportValidationException($"The file is not valid connector JSON: {ex.Message}", ex);
        }

        return await ImportPackageAsync(package, fileName, cancellationToken);
    }

    public async Task<WearableImportResult> ImportPackageAsync(
        WearableImportPackage? package,
        string fileName,
        CancellationToken cancellationToken)
    {
        ValidatePackage(package);

        for (var attempt = 1; attempt <= MaximumConcurrencyAttempts; attempt++)
        {
            try
            {
                return await ImportPackageCoreAsync(package!, fileName, cancellationToken);
            }
            catch (PostgresException ex) when (
                attempt < MaximumConcurrencyAttempts && IsRetryableConcurrencyError(ex))
            {
                await PrepareConcurrencyRetryAsync(attempt, cancellationToken);
            }
            catch (DbUpdateException ex) when (
                attempt < MaximumConcurrencyAttempts
                && ex.InnerException is PostgresException postgresException
                && IsRetryableConcurrencyError(postgresException))
            {
                await PrepareConcurrencyRetryAsync(attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException("The import retry loop completed unexpectedly.");
    }

    private async Task<WearableImportResult> ImportPackageCoreAsync(
        WearableImportPackage package,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var batch = new ImportBatch
        {
            Source = package.Source.Trim(),
            FileName = Path.GetFileName(fileName),
            SchemaVersion = package.SchemaVersion,
            ExportedAt = package.ExportedAt.ToUniversalTime(),
            ImportedAt = DateTimeOffset.UtcNow,
            TotalRecords = package.Workouts.Count
                + package.DailySummaries.Count
                + package.BodyMeasurements.Count
        };

        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        var sourceRecordIds = package.Workouts
            .Where(w => !string.IsNullOrWhiteSpace(w.SourceRecordId))
            .Select(w => w.SourceRecordId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingWorkouts = await _db.ExternalWorkouts
            .Where(w => w.Source == batch.Source && sourceRecordIds.Contains(w.SourceRecordId))
            .ToDictionaryAsync(w => w.SourceRecordId, StringComparer.Ordinal, cancellationToken);
        var workoutsWithValidTimes = package.Workouts
            .Where(workout =>
                workout.StartedAt != default
                && workout.EndedAt > workout.StartedAt)
            .ToList();
        var existingWorkoutsByTime = new List<ExternalWorkout>();
        if (workoutsWithValidTimes.Count > 0)
        {
            var earliestStart = workoutsWithValidTimes
                .Min(workout => workout.StartedAt.ToUniversalTime())
                .Subtract(WorkoutStartTolerance);
            var latestStart = workoutsWithValidTimes
                .Max(workout => workout.StartedAt.ToUniversalTime())
                .Add(WorkoutStartTolerance);
            existingWorkoutsByTime = await _db.ExternalWorkouts
                .Where(workout =>
                    workout.Source == batch.Source
                    && workout.StartedAt >= earliestStart
                    && workout.StartedAt <= latestStart)
                .ToListAsync(cancellationToken);
        }

        var seenRecordIds = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var workout in package.Workouts)
        {
            var validationError = ValidateWorkout(workout);
            if (validationError is not null)
            {
                batch.RejectedRecords++;
                errors.Add(validationError);
                continue;
            }

            var sourceRecordId = workout.SourceRecordId.Trim();
            if (!seenRecordIds.Add(sourceRecordId))
            {
                batch.RejectedRecords++;
                errors.Add($"{sourceRecordId}: duplicate record ID within the import file.");
                continue;
            }

            if (!existingWorkouts.TryGetValue(sourceRecordId, out var entity))
            {
                entity = existingWorkoutsByTime.FirstOrDefault(existing =>
                    IsSameWorkout(existing, workout));
                if (entity is not null)
                {
                    existingWorkouts.Add(sourceRecordId, entity);
                }
                else
                {
                    entity = new ExternalWorkout
                    {
                        Source = batch.Source,
                        SourceRecordId = sourceRecordId
                    };
                    _db.ExternalWorkouts.Add(entity);
                    existingWorkouts.Add(sourceRecordId, entity);
                    existingWorkoutsByTime.Add(entity);
                    MapWorkout(entity, workout, batch.Id);
                    batch.InsertedRecords++;
                    continue;
                }
            }

            if (!ShouldUpdate(entity, workout))
            {
                batch.UnchangedRecords++;
                continue;
            }

            MapWorkout(entity, workout, batch.Id);
            batch.UpdatedRecords++;
        }

        var summaryDates = package.DailySummaries
            .Select(summary => summary.Date)
            .Distinct()
            .ToList();
        var existingSummaries = await _db.ExternalDailySummaries
            .Where(summary => summary.Source == batch.Source && summaryDates.Contains(summary.SummaryDate))
            .ToDictionaryAsync(summary => summary.SummaryDate, cancellationToken);
        var seenSummaryDates = new HashSet<DateOnly>();

        foreach (var summary in package.DailySummaries)
        {
            var validationError = ValidateDailySummary(summary);
            if (validationError is not null)
            {
                batch.RejectedRecords++;
                errors.Add(validationError);
                continue;
            }

            if (!seenSummaryDates.Add(summary.Date))
            {
                batch.RejectedRecords++;
                errors.Add($"{summary.Date:yyyy-MM-dd}: duplicate daily summary within the import file.");
                continue;
            }

            var exportedAt = package.ExportedAt.ToUniversalTime();
            if (!existingSummaries.TryGetValue(summary.Date, out var entity))
            {
                entity = new ExternalDailySummary
                {
                    Source = batch.Source,
                    SummaryDate = summary.Date
                };
                _db.ExternalDailySummaries.Add(entity);
                existingSummaries.Add(summary.Date, entity);
                MapDailySummary(entity, summary, exportedAt, batch.Id);
                batch.InsertedRecords++;
                continue;
            }

            if (entity.SourceExportedAt.HasValue && exportedAt < entity.SourceExportedAt)
            {
                batch.UnchangedRecords++;
                continue;
            }

            if (entity.SourceExportedAt == exportedAt && DailySummaryMatches(entity, summary))
            {
                batch.UnchangedRecords++;
                continue;
            }

            MapDailySummary(entity, summary, exportedAt, batch.Id);
            batch.UpdatedRecords++;
        }

        var bodyRecordIds = package.BodyMeasurements
            .Where(measurement => !string.IsNullOrWhiteSpace(measurement.SourceRecordId))
            .Select(measurement => measurement.SourceRecordId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var existingBodyMeasurements = await _db.ExternalBodyMeasurements
            .Where(measurement =>
                measurement.Source == batch.Source
                && bodyRecordIds.Contains(measurement.SourceRecordId))
            .ToDictionaryAsync(
                measurement => measurement.SourceRecordId,
                StringComparer.Ordinal,
                cancellationToken);
        var seenBodyRecordIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var measurement in package.BodyMeasurements)
        {
            var validationError = ValidateBodyMeasurement(measurement);
            if (validationError is not null)
            {
                batch.RejectedRecords++;
                errors.Add(validationError);
                continue;
            }

            var sourceRecordId = measurement.SourceRecordId.Trim();
            if (!seenBodyRecordIds.Add(sourceRecordId))
            {
                batch.RejectedRecords++;
                errors.Add($"{sourceRecordId}: duplicate body measurement within the import file.");
                continue;
            }

            if (!existingBodyMeasurements.TryGetValue(sourceRecordId, out var entity))
            {
                entity = new ExternalBodyMeasurement
                {
                    Source = batch.Source,
                    SourceRecordId = sourceRecordId
                };
                _db.ExternalBodyMeasurements.Add(entity);
                existingBodyMeasurements.Add(sourceRecordId, entity);
                MapBodyMeasurement(entity, measurement, batch.Id);
                batch.InsertedRecords++;
                continue;
            }

            if (!ShouldUpdate(entity, measurement))
            {
                batch.UnchangedRecords++;
                continue;
            }

            MapBodyMeasurement(entity, measurement, batch.Id);
            batch.UpdatedRecords++;
        }

        batch.Status = batch.RejectedRecords == 0
            ? ImportStatuses.Completed
            : ImportStatuses.Partial;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WearableImportResult(batch, errors);
    }

    private async Task PrepareConcurrencyRetryAsync(
        int attempt,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
    }

    private static bool IsRetryableConcurrencyError(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.UniqueViolation;

    private static void ValidatePackage(WearableImportPackage? package)
    {
        if (package is null)
            throw new WearableImportValidationException("The import file is empty.");

        if (package.SchemaVersion is < 1 or > SupportedSchemaVersion)
        {
            throw new WearableImportValidationException(
                $"Schema version {package.SchemaVersion} is not supported. Expected version 1 or {SupportedSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(package.Source) || package.Source.Length > 50)
            throw new WearableImportValidationException("Source is required and must be 50 characters or fewer.");

        if (package.ExportedAt == default)
            throw new WearableImportValidationException("ExportedAt is required.");

        if (package.Workouts is null)
            throw new WearableImportValidationException("Workouts must be a JSON array.");

        if (package.DailySummaries is null)
            throw new WearableImportValidationException("DailySummaries must be a JSON array.");

        if (package.BodyMeasurements is null)
            throw new WearableImportValidationException("BodyMeasurements must be a JSON array.");

        var recordCount = package.Workouts.Count
            + package.DailySummaries.Count
            + package.BodyMeasurements.Count;
        if (recordCount > MaximumRecordsPerImport)
        {
            throw new WearableImportValidationException(
                $"An import can contain at most {MaximumRecordsPerImport:N0} records.");
        }
    }

    private static string? ValidateWorkout(WearableWorkoutRecord workout)
    {
        var recordId = string.IsNullOrWhiteSpace(workout.SourceRecordId)
            ? "(missing ID)"
            : workout.SourceRecordId.Trim();

        if (string.IsNullOrWhiteSpace(workout.SourceRecordId) || workout.SourceRecordId.Length > 200)
            return $"{recordId}: SourceRecordId is required and must be 200 characters or fewer.";

        if (string.IsNullOrWhiteSpace(workout.ActivityType) || workout.ActivityType.Length > 50)
            return $"{recordId}: ActivityType is required and must be 50 characters or fewer.";

        if (workout.SourceApplication?.Length > 100)
            return $"{recordId}: SourceApplication must be 100 characters or fewer.";

        if (workout.StartedAt == default || workout.EndedAt == default)
            return $"{recordId}: StartedAt and EndedAt are required.";

        if (workout.EndedAt <= workout.StartedAt)
            return $"{recordId}: EndedAt must be after StartedAt.";

        if (workout.EndedAt - workout.StartedAt > TimeSpan.FromDays(7))
            return $"{recordId}: Workout duration cannot exceed seven days.";

        if (workout.DistanceMeters is < 0)
            return $"{recordId}: DistanceMeters cannot be negative.";

        if (workout.CaloriesKcal is < 0)
            return $"{recordId}: CaloriesKcal cannot be negative.";

        if (workout.AverageHeartRateBpm is < 1 or > 300)
            return $"{recordId}: AverageHeartRateBpm must be between 1 and 300.";

        if (workout.MaxHeartRateBpm is < 1 or > 300)
            return $"{recordId}: MaxHeartRateBpm must be between 1 and 300.";

        if (workout.AverageHeartRateBpm > workout.MaxHeartRateBpm)
            return $"{recordId}: AverageHeartRateBpm cannot exceed MaxHeartRateBpm.";

        if (workout.MinHeartRateBpm is < 1 or > 300)
            return $"{recordId}: MinHeartRateBpm must be between 1 and 300.";

        if (workout.MinHeartRateBpm > workout.AverageHeartRateBpm)
            return $"{recordId}: MinHeartRateBpm cannot exceed AverageHeartRateBpm.";

        if (workout.HeartRateSamples.Count > 10_000)
            return $"{recordId}: HeartRateSamples cannot contain more than 10,000 samples.";

        if (workout.HeartRateSamples.Any(sample =>
                sample.Time < workout.StartedAt
                || sample.Time > workout.EndedAt
                || sample.BeatsPerMinute is < 1 or > 300))
        {
            return $"{recordId}: Heart-rate samples must be inside the workout and between 1 and 300 bpm.";
        }

        return null;
    }

    private static string? ValidateDailySummary(WearableDailySummaryRecord summary)
    {
        var id = summary.Date == default ? "(missing date)" : summary.Date.ToString("yyyy-MM-dd");
        if (summary.Date == default)
            return $"{id}: Date is required.";
        if (summary.Steps is < 0)
            return $"{id}: Steps cannot be negative.";
        if (summary.ActiveCaloriesKcal is < 0 || summary.TotalCaloriesKcal is < 0)
            return $"{id}: Calories cannot be negative.";
        if (summary.RestingHeartRateBpm is < 1 or > 300)
            return $"{id}: RestingHeartRateBpm must be between 1 and 300.";
        if (summary.HeartRateVariabilityRmssdMs is < 0 or > 1_000)
            return $"{id}: HeartRateVariabilityRmssdMs must be between 0 and 1,000.";
        if (summary.OxygenSaturationPercent is < 0 or > 100)
            return $"{id}: OxygenSaturationPercent must be between 0 and 100.";
        if (summary.Vo2MaxMlPerKgPerMin is < 0 or > 100)
            return $"{id}: Vo2MaxMlPerKgPerMin must be between 0 and 100.";
        if (new[]
            {
                summary.SleepDurationMinutes,
                summary.AwakeMinutes,
                summary.LightSleepMinutes,
                summary.DeepSleepMinutes,
                summary.RemSleepMinutes
            }.Any(value => value is < 0 or > 1_440))
        {
            return $"{id}: Sleep durations must be between 0 and 1,440 minutes.";
        }

        return null;
    }

    private static string? ValidateBodyMeasurement(WearableBodyMeasurementRecord measurement)
    {
        var recordId = string.IsNullOrWhiteSpace(measurement.SourceRecordId)
            ? "(missing ID)"
            : measurement.SourceRecordId.Trim();
        if (string.IsNullOrWhiteSpace(measurement.SourceRecordId)
            || measurement.SourceRecordId.Length > 200)
        {
            return $"{recordId}: SourceRecordId is required and must be 200 characters or fewer.";
        }
        if (measurement.SourceApplication?.Length > 100)
            return $"{recordId}: SourceApplication must be 100 characters or fewer.";
        if (measurement.MeasuredAt == default)
            return $"{recordId}: MeasuredAt is required.";
        if (measurement.WeightKg is <= 0 or > 1_000)
            return $"{recordId}: WeightKg must be greater than 0 and no more than 1,000.";
        if (measurement.BodyFatPercent is < 0 or > 100)
            return $"{recordId}: BodyFatPercent must be between 0 and 100.";
        if (!measurement.WeightKg.HasValue && !measurement.BodyFatPercent.HasValue)
            return $"{recordId}: At least one body measurement value is required.";

        return null;
    }

    private static bool ShouldUpdate(ExternalWorkout existing, WearableWorkoutRecord incoming)
    {
        var incomingModifiedAt = incoming.LastModifiedAt?.ToUniversalTime();
        if (incomingModifiedAt.HasValue)
        {
            if (!existing.SourceLastModifiedAt.HasValue
                || incomingModifiedAt > existing.SourceLastModifiedAt)
            {
                return true;
            }

            if (incomingModifiedAt < existing.SourceLastModifiedAt)
                return false;
        }
        else if (existing.SourceLastModifiedAt.HasValue)
        {
            return false;
        }

        return existing.SourceApplication != NullIfWhiteSpace(incoming.SourceApplication)
            || existing.ActivityType != incoming.ActivityType.Trim()
            || existing.StartedAt != incoming.StartedAt.ToUniversalTime()
            || existing.EndedAt != incoming.EndedAt.ToUniversalTime()
            || existing.DistanceMeters != incoming.DistanceMeters
            || existing.CaloriesKcal != incoming.CaloriesKcal
            || existing.MinHeartRateBpm != incoming.MinHeartRateBpm
            || existing.AverageHeartRateBpm != incoming.AverageHeartRateBpm
            || existing.MaxHeartRateBpm != incoming.MaxHeartRateBpm
            || !JsonEquals(
                existing.HeartRateSamples,
                SerializeHeartRateSamples(incoming.HeartRateSamples));
    }

    private static bool IsSameWorkout(
        ExternalWorkout existing,
        WearableWorkoutRecord incoming)
    {
        var startedAt = incoming.StartedAt.ToUniversalTime();
        var endedAt = incoming.EndedAt.ToUniversalTime();
        return (existing.StartedAt - startedAt).Duration() <= WorkoutStartTolerance
            && existing.StartedAt < endedAt
            && startedAt < existing.EndedAt;
    }

    private static bool DailySummaryMatches(
        ExternalDailySummary existing,
        WearableDailySummaryRecord incoming) =>
        existing.Steps == incoming.Steps
        && existing.ActiveCaloriesKcal == incoming.ActiveCaloriesKcal
        && existing.TotalCaloriesKcal == incoming.TotalCaloriesKcal
        && existing.DistanceMeters == incoming.DistanceMeters
        && existing.RestingHeartRateBpm == incoming.RestingHeartRateBpm
        && existing.HeartRateVariabilityRmssdMs == incoming.HeartRateVariabilityRmssdMs
        && existing.OxygenSaturationPercent == incoming.OxygenSaturationPercent
        && existing.Vo2MaxMlPerKgPerMin == incoming.Vo2MaxMlPerKgPerMin
        && existing.SleepDurationMinutes == incoming.SleepDurationMinutes
        && existing.AwakeMinutes == incoming.AwakeMinutes
        && existing.LightSleepMinutes == incoming.LightSleepMinutes
        && existing.DeepSleepMinutes == incoming.DeepSleepMinutes
        && existing.RemSleepMinutes == incoming.RemSleepMinutes;

    private static bool ShouldUpdate(
        ExternalBodyMeasurement existing,
        WearableBodyMeasurementRecord incoming)
    {
        var incomingModifiedAt = incoming.LastModifiedAt?.ToUniversalTime();
        if (incomingModifiedAt.HasValue)
        {
            if (!existing.SourceLastModifiedAt.HasValue
                || incomingModifiedAt > existing.SourceLastModifiedAt)
            {
                return true;
            }

            if (incomingModifiedAt < existing.SourceLastModifiedAt)
                return false;
        }
        else if (existing.SourceLastModifiedAt.HasValue)
        {
            return false;
        }

        return !BodyMeasurementMatches(existing, incoming);
    }

    private static bool BodyMeasurementMatches(
        ExternalBodyMeasurement existing,
        WearableBodyMeasurementRecord incoming) =>
        existing.SourceApplication == NullIfWhiteSpace(incoming.SourceApplication)
        && existing.MeasuredAt == incoming.MeasuredAt.ToUniversalTime()
        && existing.SourceLastModifiedAt == incoming.LastModifiedAt?.ToUniversalTime()
        && existing.WeightKg == incoming.WeightKg
        && existing.BodyFatPercent == incoming.BodyFatPercent;

    private static bool JsonEquals(string existingJson, string incomingJson)
    {
        using var existing = JsonDocument.Parse(existingJson);
        using var incoming = JsonDocument.Parse(incomingJson);
        return JsonElement.DeepEquals(existing.RootElement, incoming.RootElement);
    }

    private static void MapWorkout(
        ExternalWorkout entity,
        WearableWorkoutRecord workout,
        int importBatchId)
    {
        entity.SourceApplication = NullIfWhiteSpace(workout.SourceApplication);
        entity.ActivityType = workout.ActivityType.Trim();
        entity.StartedAt = workout.StartedAt.ToUniversalTime();
        entity.EndedAt = workout.EndedAt.ToUniversalTime();
        if (workout.LastModifiedAt.HasValue || !entity.SourceLastModifiedAt.HasValue)
            entity.SourceLastModifiedAt = workout.LastModifiedAt?.ToUniversalTime();
        entity.DistanceMeters = workout.DistanceMeters;
        entity.CaloriesKcal = workout.CaloriesKcal;
        entity.MinHeartRateBpm = workout.MinHeartRateBpm;
        entity.AverageHeartRateBpm = workout.AverageHeartRateBpm;
        entity.MaxHeartRateBpm = workout.MaxHeartRateBpm;
        entity.HeartRateSampleCount = workout.HeartRateSamples.Count;
        entity.HeartRateSamples = SerializeHeartRateSamples(workout.HeartRateSamples);
        entity.LastImportBatchId = importBatchId;
        entity.RawPayload = JsonSerializer.Serialize(workout, JsonOptions);
    }

    private static void MapDailySummary(
        ExternalDailySummary entity,
        WearableDailySummaryRecord summary,
        DateTimeOffset exportedAt,
        int importBatchId)
    {
        entity.SourceExportedAt = exportedAt;
        entity.Steps = summary.Steps;
        entity.ActiveCaloriesKcal = summary.ActiveCaloriesKcal;
        entity.TotalCaloriesKcal = summary.TotalCaloriesKcal;
        entity.DistanceMeters = summary.DistanceMeters;
        entity.RestingHeartRateBpm = summary.RestingHeartRateBpm;
        entity.HeartRateVariabilityRmssdMs = summary.HeartRateVariabilityRmssdMs;
        entity.OxygenSaturationPercent = summary.OxygenSaturationPercent;
        entity.Vo2MaxMlPerKgPerMin = summary.Vo2MaxMlPerKgPerMin;
        entity.SleepDurationMinutes = summary.SleepDurationMinutes;
        entity.AwakeMinutes = summary.AwakeMinutes;
        entity.LightSleepMinutes = summary.LightSleepMinutes;
        entity.DeepSleepMinutes = summary.DeepSleepMinutes;
        entity.RemSleepMinutes = summary.RemSleepMinutes;
        entity.LastImportBatchId = importBatchId;
        entity.RawPayload = JsonSerializer.Serialize(summary, JsonOptions);
    }

    private static void MapBodyMeasurement(
        ExternalBodyMeasurement entity,
        WearableBodyMeasurementRecord measurement,
        int importBatchId)
    {
        entity.SourceApplication = NullIfWhiteSpace(measurement.SourceApplication);
        entity.MeasuredAt = measurement.MeasuredAt.ToUniversalTime();
        entity.SourceLastModifiedAt = measurement.LastModifiedAt?.ToUniversalTime();
        entity.WeightKg = measurement.WeightKg;
        entity.BodyFatPercent = measurement.BodyFatPercent;
        entity.LastImportBatchId = importBatchId;
        entity.RawPayload = JsonSerializer.Serialize(measurement, JsonOptions);
    }

    private static string SerializeHeartRateSamples(
        IReadOnlyList<WearableHeartRateSample> samples) =>
        JsonSerializer.Serialize(samples, JsonOptions);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class WearableImportPackage
{
    public int SchemaVersion { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset ExportedAt { get; set; }
    public List<WearableWorkoutRecord> Workouts { get; set; } = [];
    public List<WearableDailySummaryRecord> DailySummaries { get; set; } = [];
    public List<WearableBodyMeasurementRecord> BodyMeasurements { get; set; } = [];
}

public sealed class WearableWorkoutRecord
{
    public string SourceRecordId { get; set; } = string.Empty;
    public string? SourceApplication { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public decimal? DistanceMeters { get; set; }
    public decimal? CaloriesKcal { get; set; }
    public int? MinHeartRateBpm { get; set; }
    public int? AverageHeartRateBpm { get; set; }
    public int? MaxHeartRateBpm { get; set; }
    public List<WearableHeartRateSample> HeartRateSamples { get; set; } = [];
}

public sealed class WearableHeartRateSample
{
    public DateTimeOffset Time { get; set; }
    public int BeatsPerMinute { get; set; }
}

public sealed class WearableDailySummaryRecord
{
    public DateOnly Date { get; set; }
    public long? Steps { get; set; }
    public decimal? ActiveCaloriesKcal { get; set; }
    public decimal? TotalCaloriesKcal { get; set; }
    public decimal? DistanceMeters { get; set; }
    public int? RestingHeartRateBpm { get; set; }
    public decimal? HeartRateVariabilityRmssdMs { get; set; }
    public decimal? OxygenSaturationPercent { get; set; }
    public decimal? Vo2MaxMlPerKgPerMin { get; set; }
    public int? SleepDurationMinutes { get; set; }
    public int? AwakeMinutes { get; set; }
    public int? LightSleepMinutes { get; set; }
    public int? DeepSleepMinutes { get; set; }
    public int? RemSleepMinutes { get; set; }
}

public sealed class WearableBodyMeasurementRecord
{
    public string SourceRecordId { get; set; } = string.Empty;
    public string? SourceApplication { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
}

public sealed record WearableImportResult(ImportBatch Batch, IReadOnlyList<string> Errors);

public sealed class WearableImportValidationException : Exception
{
    public WearableImportValidationException(string message) : base(message) { }
    public WearableImportValidationException(string message, Exception innerException) : base(message, innerException) { }
}
