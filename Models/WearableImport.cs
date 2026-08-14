using System.ComponentModel.DataAnnotations;

namespace TennisIntelligence.Models;

public sealed class ImportBatch
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required, MaxLength(20)]
    public string Status { get; set; } = ImportStatuses.Completed;

    public int TotalRecords { get; set; }
    public int InsertedRecords { get; set; }
    public int UpdatedRecords { get; set; }
    public int UnchangedRecords { get; set; }
    public int RejectedRecords { get; set; }

    /// <summary>
    /// Why records were rejected. A rejection is never retried, so without this the reason is lost
    /// for background syncs, which report only counts.
    /// </summary>
    [MaxLength(4000)]
    public string? RejectionReasons { get; set; }
}

public sealed class ExternalWorkout
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SourceRecordId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SourceApplication { get; set; }

    [Required, MaxLength(50)]
    public string ActivityType { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public DateTimeOffset? SourceLastModifiedAt { get; set; }
    public decimal? DistanceMeters { get; set; }
    public decimal? CaloriesKcal { get; set; }
    public int? AverageHeartRateBpm { get; set; }
    public int? MinHeartRateBpm { get; set; }
    public int? MaxHeartRateBpm { get; set; }
    public int HeartRateSampleCount { get; set; }

    [Required]
    public string HeartRateSamples { get; set; } = "[]";

    public int LastImportBatchId { get; set; }
    public ImportBatch LastImportBatch { get; set; } = null!;

    [Required]
    public string RawPayload { get; set; } = "{}";
}

public sealed class ExternalDailySummary
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    public DateOnly SummaryDate { get; set; }
    public DateTimeOffset? SourceExportedAt { get; set; }
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
    public int LastImportBatchId { get; set; }
    public ImportBatch LastImportBatch { get; set; } = null!;

    [Required]
    public string RawPayload { get; set; } = "{}";
}

public sealed class ExternalBodyMeasurement
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SourceRecordId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? SourceApplication { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }
    public DateTimeOffset? SourceLastModifiedAt { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public int LastImportBatchId { get; set; }
    public ImportBatch LastImportBatch { get; set; } = null!;

    [Required]
    public string RawPayload { get; set; } = "{}";
}

public static class ImportStatuses
{
    public const string Completed = "Completed";
    public const string Partial = "Partial";
}
