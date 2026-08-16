namespace TennisIntelligence.Services;

/// <summary>One rise in heart rate and the recovery that followed it.</summary>
public sealed class WorkBout
{
    public double WorkSeconds { get; set; }
    public int RiseBpm { get; set; }

    /// <summary>Seconds spent coming back down afterwards, or null for the last bout of a session.</summary>
    public double? RecoverySeconds { get; set; }
    public int? DropBpm { get; set; }
}

/// <summary>
/// The shape of a session's effort: how often the heart rate was driven up, how hard, and how long
/// it took to come back down.
/// </summary>
/// <remarks>
/// Heart rate lags the effort that caused it by several seconds and smooths short bursts together,
/// so a bout is the cardiac response to a passage of play rather than a single point. Treat the
/// numbers as the rhythm of the session, not a rally stopwatch.
/// </remarks>
public sealed class SessionRhythm
{
    public int BoutCount { get; set; }
    public double MedianWorkSeconds { get; set; }
    public double? MedianRecoverySeconds { get; set; }
    public int MedianRiseBpm { get; set; }

    /// <summary>Work seconds per second of recovery. Above 1 means more pushing than resting.</summary>
    public double? WorkToRestRatio { get; set; }

    /// <summary>Bouts per hour, so sessions of different lengths compare.</summary>
    public double BoutsPerHour { get; set; }
}

/// <summary>
/// Finds the sawtooth in a heart-rate series: the alternating climbs and recoveries that a stop-start
/// sport produces and a steady run does not.
/// </summary>
public static class RhythmAnalytics
{
    /// <summary>A reversal smaller than this is sensor noise rather than a change of effort.</summary>
    private const int MinProminenceBpm = 6;

    /// <summary>Samples averaged to remove jitter without flattening a passage of play.</summary>
    private const int SmoothingWindow = 5;

    /// <summary>Below this a session is too short or too flat for the shape to mean anything.</summary>
    private const int MinBouts = 5;

    /// <summary>A gap longer than this is a pause in recording, not a recovery.</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Longer than this is a break — a drink, a chat, a changeover — not recovery between efforts.
    /// Counting them made late-session recovery look minutes longer than early-session recovery.
    /// </summary>
    private const double MaxRecoverySeconds = 180;

    public static SessionRhythm? Analyse(IReadOnlyList<HeartRateSample> samples) =>
        Analyse(samples, MinProminenceBpm, SmoothingWindow);

    /// <summary>Calibration entry point. The defaults were chosen by sweeping these against real sessions.</summary>
    public static SessionRhythm? Analyse(IReadOnlyList<HeartRateSample> samples, int prominenceBpm, int smoothingWindow)
    {
        if (samples.Count < 60) return null;

        var smoothed = Smooth(samples, smoothingWindow);
        var extrema = FindExtrema(smoothed, prominenceBpm);
        var bouts = BuildBouts(extrema);

        if (bouts.Count < MinBouts) return null;

        var works = bouts.Select(b => b.WorkSeconds).OrderBy(v => v).ToList();
        var recoveries = bouts.Where(b => b.RecoverySeconds.HasValue)
            .Select(b => b.RecoverySeconds!.Value)
            .OrderBy(v => v)
            .ToList();
        var rises = bouts.Select(b => b.RiseBpm).OrderBy(v => v).ToList();

        var totalSeconds = (smoothed[^1].Time - smoothed[0].Time).TotalSeconds;
        var medianWork = Median(works);
        var medianRecovery = recoveries.Count > 0 ? Median(recoveries) : (double?)null;

        return new SessionRhythm
        {
            BoutCount = bouts.Count,
            MedianWorkSeconds = Math.Round(medianWork, 1),
            MedianRecoverySeconds = medianRecovery is double r ? Math.Round(r, 1) : null,
            MedianRiseBpm = rises[rises.Count / 2],
            WorkToRestRatio = medianRecovery is > 0 ? Math.Round(medianWork / medianRecovery.Value, 2) : null,
            BoutsPerHour = totalSeconds > 0 ? Math.Round(bouts.Count / (totalSeconds / 3600.0), 1) : 0
        };
    }

    private static List<HeartRateSample> Smooth(IReadOnlyList<HeartRateSample> samples, int window)
    {
        var result = new List<HeartRateSample>(samples.Count);
        var half = window / 2;

        for (var i = 0; i < samples.Count; i++)
        {
            var from = Math.Max(0, i - half);
            var to = Math.Min(samples.Count - 1, i + half);
            var sum = 0;
            for (var j = from; j <= to; j++) sum += samples[j].BeatsPerMinute;

            result.Add(new HeartRateSample
            {
                Time = samples[i].Time,
                BeatsPerMinute = (int)Math.Round((double)sum / (to - from + 1))
            });
        }

        return result;
    }

    /// <summary>
    /// Walks the series keeping the most extreme point seen since the last turn, and only accepts a
    /// turn once the series has moved back far enough to rule out noise.
    /// </summary>
    private static List<(HeartRateSample Sample, bool IsPeak)> FindExtrema(List<HeartRateSample> samples, int prominenceBpm)
    {
        var extrema = new List<(HeartRateSample, bool)>();
        var pivot = samples[0];
        var rising = true;

        foreach (var sample in samples.Skip(1))
        {
            // A recording gap breaks the sawtooth: restart rather than invent a long bout across it.
            if (sample.Time - pivot.Time > MaxGap)
            {
                pivot = sample;
                continue;
            }

            if (rising)
            {
                if (sample.BeatsPerMinute >= pivot.BeatsPerMinute)
                {
                    pivot = sample;
                }
                else if (pivot.BeatsPerMinute - sample.BeatsPerMinute >= prominenceBpm)
                {
                    extrema.Add((pivot, true));
                    pivot = sample;
                    rising = false;
                }
            }
            else
            {
                if (sample.BeatsPerMinute <= pivot.BeatsPerMinute)
                {
                    pivot = sample;
                }
                else if (sample.BeatsPerMinute - pivot.BeatsPerMinute >= prominenceBpm)
                {
                    extrema.Add((pivot, false));
                    pivot = sample;
                    rising = true;
                }
            }
        }

        return extrema;
    }

    private static List<WorkBout> BuildBouts(List<(HeartRateSample Sample, bool IsPeak)> extrema)
    {
        var bouts = new List<WorkBout>();

        for (var i = 0; i < extrema.Count - 1; i++)
        {
            var (start, startIsPeak) = extrema[i];
            if (startIsPeak) continue;

            var (peak, peakIsPeak) = extrema[i + 1];
            if (!peakIsPeak) continue;

            var bout = new WorkBout
            {
                WorkSeconds = (peak.Time - start.Time).TotalSeconds,
                RiseBpm = peak.BeatsPerMinute - start.BeatsPerMinute
            };

            if (i + 2 < extrema.Count)
            {
                var (valley, valleyIsPeak) = extrema[i + 2];
                if (!valleyIsPeak)
                {
                    var recovery = (valley.Time - peak.Time).TotalSeconds;
                    if (recovery <= MaxRecoverySeconds)
                    {
                        bout.RecoverySeconds = recovery;
                        bout.DropBpm = peak.BeatsPerMinute - valley.BeatsPerMinute;
                    }
                }
            }

            bouts.Add(bout);
        }

        return bouts;
    }



    private static double Median(List<double> ordered) => ordered[ordered.Count / 2];
}
