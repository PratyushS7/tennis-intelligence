using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TennisIntelligence.Data;

namespace TennisIntelligence.Services;

/// <summary>One workout with its heart-rate series already interpreted.</summary>
public sealed class AnalysedWorkout
{
    public int Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int? AverageHeartRateBpm { get; set; }
    public int? MaxHeartRateBpm { get; set; }
    public WorkoutAnalysis Analysis { get; set; } = new();

    public bool IsTennis => ActivityType.Contains("tennis", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Training picture derived purely from wearable data, with no manual logging required.</summary>
public sealed class TrainingLoadReport
{
    /// <summary>Highest heart rate ever recorded, used as the zone anchor.</summary>
    public int ObservedMaxHeartRate { get; set; }

    public List<AnalysedWorkout> Workouts { get; set; } = [];

    public List<AnalysedWorkout> TennisWorkouts =>
        Workouts.Where(w => w.IsTennis && w.Analysis.HasSeries).ToList();

    /// <summary>Time-weighted zone split across every analysed tennis session.</summary>
    public ZoneDistribution TennisZones { get; set; } = new();

    public int TennisHardSessions { get; set; }
    public int TennisModerateSessions { get; set; }
    public int TennisLightSessions { get; set; }

    /// <summary>
    /// Tennis sessions hard enough for recovery to mean something, oldest first. Light sessions are
    /// excluded because a handful of borderline peaks does not compare with a real effort.
    /// </summary>
    public List<AnalysedWorkout> RecoveryTrend { get; set; } = [];

    public bool HasData => Workouts.Any(w => w.Analysis.HasSeries);
}

/// <summary>
/// Builds the wearable-derived training picture. Results are cached against the newest workout so a
/// page view does not re-download and re-parse several megabytes of heart-rate series from Neon.
/// </summary>
public sealed class TrainingLoadService
{
    /// <summary>Enough history to show a trend without unbounded parsing cost.</summary>
    private const int MaxWorkoutsAnalysed = 25;

    private readonly TennisDbContext _db;
    private readonly IMemoryCache _cache;

    public TrainingLoadService(TennisDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<TrainingLoadReport> GetReportAsync(CancellationToken ct = default)
    {
        // Keyed on the newest workout and the total, so a sync that adds or replaces rows recomputes.
        var fingerprint = await _db.ExternalWorkouts
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Max = g.Max(w => w.Id), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        if (fingerprint is null) return new TrainingLoadReport();

        var key = $"trainingload:{fingerprint.Max}:{fingerprint.Count}";
        if (_cache.TryGetValue(key, out TrainingLoadReport? cached) && cached is not null) return cached;

        var report = await BuildAsync(ct);
        _cache.Set(key, report, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
            Size = 1
        });

        return report;
    }

    private async Task<TrainingLoadReport> BuildAsync(CancellationToken ct)
    {
        var observedMax = await _db.ExternalWorkouts
            .AsNoTracking()
            .Where(w => w.MaxHeartRateBpm != null)
            .MaxAsync(w => (int?)w.MaxHeartRateBpm, ct) ?? 0;

        var report = new TrainingLoadReport { ObservedMaxHeartRate = observedMax };
        if (observedMax <= 0) return report;

        var rows = await _db.ExternalWorkouts
            .AsNoTracking()
            .Where(w => w.HeartRateSampleCount > 0)
            .OrderByDescending(w => w.StartedAt)
            .Take(MaxWorkoutsAnalysed)
            .Select(w => new
            {
                w.Id,
                w.StartedAt,
                w.EndedAt,
                w.ActivityType,
                w.AverageHeartRateBpm,
                w.MaxHeartRateBpm,
                w.HeartRateSamples
            })
            .ToListAsync(ct);

        report.Workouts = new List<AnalysedWorkout>(rows.Count);
        foreach (var row in rows)
        {
            var samples = WorkoutAnalytics.ParseSamples(row.HeartRateSamples);
            report.Workouts.Add(new AnalysedWorkout
            {
                Id = row.Id,
                StartedAt = row.StartedAt,
                ActivityType = row.ActivityType,
                DurationMinutes = (int)Math.Round((row.EndedAt - row.StartedAt).TotalMinutes),
                AverageHeartRateBpm = row.AverageHeartRateBpm,
                MaxHeartRateBpm = row.MaxHeartRateBpm,
                Analysis = WorkoutAnalytics.Analyse(samples, observedMax)
            });
        }

        Summarise(report);
        return report;
    }

    private static void Summarise(TrainingLoadReport report)
    {
        var tennis = report.TennisWorkouts;
        if (tennis.Count == 0) return;

        report.TennisHardSessions = tennis.Count(w => w.Analysis.Character == SessionCharacter.Hard);
        report.TennisModerateSessions = tennis.Count(w => w.Analysis.Character == SessionCharacter.Moderate);
        report.TennisLightSessions = tennis.Count(w => w.Analysis.Character == SessionCharacter.Light);

        // Weight each session's split by its duration so a short hit does not count as much as a long match.
        double totalMinutes = tennis.Sum(w => w.DurationMinutes);
        if (totalMinutes > 0)
        {
            report.TennisZones = new ZoneDistribution
            {
                Zone1Pct = tennis.Sum(w => w.Analysis.Zones.Zone1Pct * w.DurationMinutes) / totalMinutes,
                Zone2Pct = tennis.Sum(w => w.Analysis.Zones.Zone2Pct * w.DurationMinutes) / totalMinutes,
                Zone3Pct = tennis.Sum(w => w.Analysis.Zones.Zone3Pct * w.DurationMinutes) / totalMinutes,
                Zone4Pct = tennis.Sum(w => w.Analysis.Zones.Zone4Pct * w.DurationMinutes) / totalMinutes,
                Zone5Pct = tennis.Sum(w => w.Analysis.Zones.Zone5Pct * w.DurationMinutes) / totalMinutes
            };
        }

        // Recovery only compares like with like, so sustained runs and easy hits are both excluded.
        report.RecoveryTrend = tennis
            .Where(w => w.Analysis.HeartRateRecovery60.HasValue && w.Analysis.Character != SessionCharacter.Light)
            .OrderBy(w => w.StartedAt)
            .ToList();
    }
}
