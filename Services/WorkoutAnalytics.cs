using System.Text.Json;

namespace TennisIntelligence.Services;

/// <summary>One heart-rate reading inside a wearable workout.</summary>
public sealed class HeartRateSample
{
    public DateTimeOffset Time { get; set; }
    public int BeatsPerMinute { get; set; }
}

/// <summary>How hard a session actually was, derived from time spent near maximum heart rate.</summary>
public enum SessionCharacter
{
    Unknown,
    Light,
    Moderate,
    Hard
}

/// <summary>Share of a session spent in each heart-rate zone, as a percentage of session time.</summary>
public sealed class ZoneDistribution
{
    public double Zone1Pct { get; set; }
    public double Zone2Pct { get; set; }
    public double Zone3Pct { get; set; }
    public double Zone4Pct { get; set; }
    public double Zone5Pct { get; set; }

    /// <summary>Time at threshold or above, the part of a session that drives conditioning.</summary>
    public double HardPct => Zone4Pct + Zone5Pct;
}

/// <summary>Everything derivable from one workout's heart-rate series.</summary>
public sealed class WorkoutAnalysis
{
    public int SampleCount { get; set; }
    public bool HasSeries => SampleCount > 0;
    public ZoneDistribution Zones { get; set; } = new();
    public SessionCharacter Character { get; set; } = SessionCharacter.Unknown;

    /// <summary>
    /// Beats dropped in the 60s after a hard effort, at the 90th percentile of the session's recovery
    /// windows. Play usually continues through a peak, so the typical drop measures rally length rather
    /// than fitness; the upper percentile captures the real breaks. Higher is fitter, and unlike a
    /// readiness score it needs only in-session data.
    /// </summary>
    public int? HeartRateRecovery60 { get; set; }

    /// <summary>How many recovery windows backed <see cref="HeartRateRecovery60"/>.</summary>
    public int RecoveryWindowCount { get; set; }

    /// <summary>
    /// Average bpm in the final third minus the middle third. Positive means fading. The first third is
    /// excluded because it is warm-up, which would otherwise swamp the signal.
    /// </summary>
    public int? DriftBpm { get; set; }
}

/// <summary>
/// Derives intensity, recovery and fade from a workout's heart-rate series. Kept free of database and
/// HTTP types so it can be exercised directly against stored sessions.
/// </summary>
public static class WorkoutAnalytics
{
    /// <summary>Longest gap credited to a single sample, so a dropout cannot dominate the zone split.</summary>
    private static readonly TimeSpan MaxSampleWeight = TimeSpan.FromSeconds(10);

    /// <summary>A peak must reach zone 4 to count as an effort worth recovering from.</summary>
    private const double RecoveryEffortFloor = 0.80;

    /// <summary>Below this many windows the percentile is one lucky break, not a measurement.</summary>
    private const int MinimumRecoveryWindows = 4;

    private static readonly JsonSerializerOptions SampleJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<HeartRateSample> ParseSamples(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var samples = JsonSerializer.Deserialize<List<HeartRateSample>>(json, SampleJsonOptions);
            if (samples is null) return [];

            return samples
                .Where(sample => sample.BeatsPerMinute > 0)
                .OrderBy(sample => sample.Time)
                .ToList();
        }
        catch (JsonException)
        {
            // A malformed series must not take down the page; the workout simply has no analysis.
            return [];
        }
    }

    public static WorkoutAnalysis Analyse(IReadOnlyList<HeartRateSample> samples, int maxHeartRate)
    {
        var analysis = new WorkoutAnalysis { SampleCount = samples.Count };
        if (samples.Count < 2 || maxHeartRate <= 0) return analysis;

        analysis.Zones = ComputeZones(samples, maxHeartRate);
        analysis.Character = Classify(analysis.Zones);

        var (recovery, windows) = ComputeRecovery(samples, maxHeartRate);
        analysis.HeartRateRecovery60 = recovery;
        analysis.RecoveryWindowCount = windows;

        analysis.DriftBpm = ComputeDrift(samples);
        return analysis;
    }

    /// <summary>
    /// Weights each sample by the time it represents rather than counting samples, so irregular
    /// cadence and dropouts do not distort the split.
    /// </summary>
    private static ZoneDistribution ComputeZones(IReadOnlyList<HeartRateSample> samples, int maxHeartRate)
    {
        var seconds = new double[6];
        var total = 0.0;

        for (var i = 0; i < samples.Count; i++)
        {
            var weight = i < samples.Count - 1
                ? samples[i + 1].Time - samples[i].Time
                : MaxSampleWeight;

            if (weight <= TimeSpan.Zero) continue;
            if (weight > MaxSampleWeight) weight = MaxSampleWeight;

            var zone = ZoneOf(samples[i].BeatsPerMinute, maxHeartRate);
            seconds[zone] += weight.TotalSeconds;
            total += weight.TotalSeconds;
        }

        if (total <= 0) return new ZoneDistribution();

        return new ZoneDistribution
        {
            Zone1Pct = 100.0 * seconds[1] / total,
            Zone2Pct = 100.0 * seconds[2] / total,
            Zone3Pct = 100.0 * seconds[3] / total,
            Zone4Pct = 100.0 * seconds[4] / total,
            Zone5Pct = 100.0 * seconds[5] / total
        };
    }

    /// <summary>Standard five-zone split. Index 0 holds anything below zone 1.</summary>
    private static int ZoneOf(int bpm, int maxHeartRate)
    {
        var pct = (double)bpm / maxHeartRate;
        if (pct < 0.60) return 1;
        if (pct < 0.70) return 2;
        if (pct < 0.80) return 3;
        if (pct < 0.90) return 4;
        return 5;
    }

    private static SessionCharacter Classify(ZoneDistribution zones)
    {
        if (zones.HardPct >= 30) return SessionCharacter.Hard;
        if (zones.HardPct >= 10) return SessionCharacter.Moderate;
        return SessionCharacter.Light;
    }

    /// <summary>
    /// Finds efforts the session actually recovered from and measures the drop 60s later. Anchoring on
    /// local peaks keeps steady-state stretches from reading as recovery.
    /// </summary>
    private static (int? Recovery, int Windows) ComputeRecovery(IReadOnlyList<HeartRateSample> samples, int maxHeartRate)
    {
        var floor = maxHeartRate * RecoveryEffortFloor;
        var drops = new List<int>();
        var lastPeakTime = DateTimeOffset.MinValue;

        for (var i = 0; i < samples.Count; i++)
        {
            var peak = samples[i];
            if (peak.BeatsPerMinute < floor) continue;

            // One measurement per effort, otherwise a single peak contributes a whole plateau.
            if (peak.Time - lastPeakTime < TimeSpan.FromSeconds(60)) continue;
            if (!IsLocalPeak(samples, i)) continue;

            var target = peak.Time.AddSeconds(60);
            var after = FindNearest(samples, i, target);
            if (after is null) continue;

            drops.Add(peak.BeatsPerMinute - after.BeatsPerMinute);
            lastPeakTime = peak.Time;
        }

        if (drops.Count < MinimumRecoveryWindows) return (null, drops.Count);

        drops.Sort();
        return ((int)Math.Round(Percentile(drops, 0.90)), drops.Count);
    }

    private static bool IsLocalPeak(IReadOnlyList<HeartRateSample> samples, int index)
    {
        var window = TimeSpan.FromSeconds(15);
        var value = samples[index].BeatsPerMinute;
        var time = samples[index].Time;

        for (var i = index - 1; i >= 0 && time - samples[i].Time <= window; i--)
            if (samples[i].BeatsPerMinute > value) return false;

        for (var i = index + 1; i < samples.Count && samples[i].Time - time <= window; i++)
            if (samples[i].BeatsPerMinute > value) return false;

        return true;
    }

    /// <summary>Nearest sample to <paramref name="target"/>, or null when the series ends first.</summary>
    private static HeartRateSample? FindNearest(IReadOnlyList<HeartRateSample> samples, int from, DateTimeOffset target)
    {
        var tolerance = TimeSpan.FromSeconds(10);
        for (var i = from + 1; i < samples.Count; i++)
        {
            var delta = samples[i].Time - target;
            if (delta >= -tolerance && delta <= tolerance) return samples[i];
            if (delta > tolerance) return null;
        }
        return null;
    }

    /// <summary>
    /// Average bpm of the final third minus the middle third. Warm-up sits in the first third and would
    /// otherwise read as a large positive drift on every session.
    /// </summary>
    private static int? ComputeDrift(IReadOnlyList<HeartRateSample> samples)
    {
        var start = samples[0].Time;
        var span = samples[^1].Time - start;
        if (span < TimeSpan.FromMinutes(15)) return null;

        var third = span / 3;
        var middleStart = start + third;
        var lastStart = middleStart + third;

        var middle = samples.Where(s => s.Time >= middleStart && s.Time < lastStart).Select(s => s.BeatsPerMinute).ToList();
        var last = samples.Where(s => s.Time >= lastStart).Select(s => s.BeatsPerMinute).ToList();
        if (middle.Count == 0 || last.Count == 0) return null;

        return (int)Math.Round(last.Average() - middle.Average());
    }

    /// <summary>Linear-interpolated percentile over an ascending list.</summary>
    private static double Percentile(List<int> sorted, double fraction)
    {
        if (sorted.Count == 1) return sorted[0];

        var position = fraction * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];

        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
