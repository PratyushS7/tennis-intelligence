namespace TennisIntelligence.Services;

/// <summary>
/// Turns a <see cref="TrainingLoadReport"/> into plain sentences. Kept in one place so the coach
/// and the phone say the same thing, and so the wording can change without shipping a new APK.
/// </summary>
public static class TrainingLoadNarrative
{
    /// <summary>Below this share of threshold time a session is rallying rather than training.</summary>
    private const double EasyThresholdPct = 15;

    /// <summary>Above this share the hard sessions stop being productive without an easy one.</summary>
    private const double HardThresholdPct = 50;

    /// <summary>Smaller recovery or drift changes than this are noise, not a trend.</summary>
    private const int MeaningfulBpmChange = 3;
    private const int MeaningfulDriftBpm = 5;

    /// <summary>The load picture as display lines, most important first.</summary>
    public static List<string> Describe(TrainingLoadReport report)
    {
        var lines = new List<string>();
        if (!report.HasData) return lines;

        var tennis = report.TennisWorkouts;
        if (tennis.Count > 0)
        {
            lines.Add($"{tennis.Count} analysed tennis session(s), zones anchored to your observed max of {report.ObservedMaxHeartRate} bpm.");
            lines.Add($"Intensity mix: {report.TennisHardSessions} hard, {report.TennisModerateSessions} moderate, {report.TennisLightSessions} light.");
            lines.Add($"Time at threshold or above: {report.TennisZones.HardPct:F0}% of tennis time.");

            if (report.TennisZones.HardPct < EasyThresholdPct)
                lines.Add("That's low. Your sessions are mostly rallying rather than pushing, which is fine for feel and volume but won't move your fitness.");
            else if (report.TennisZones.HardPct > HardThresholdPct)
                lines.Add("That's a lot of threshold work. Keep at least one session a week genuinely easy, or the hard ones stop being productive.");
        }

        AppendRecovery(lines, report);
        AppendDrift(lines, report);
        AppendRhythm(lines, report);
        return lines;
    }

    /// <summary>
    /// Describes how continuously a session was played. Heart rate lags effort by several seconds
    /// and smooths short bursts together, so this is the rhythm of passages of play, never a count
    /// of points, and the wording must not imply otherwise.
    /// </summary>
    private static void AppendRhythm(List<string> lines, TrainingLoadReport report)
    {
        var rhythms = report.TennisWorkouts
            .Select(w => w.Analysis.Rhythm)
            .Where(r => r is not null && r.WorkToRestRatio.HasValue)
            .Select(r => r!)
            .ToList();

        if (rhythms.Count < 3) return;

        var ratios = rhythms.Select(r => r.WorkToRestRatio!.Value).OrderBy(v => v).ToList();
        var median = ratios[ratios.Count / 2];

        lines.Add($"Session rhythm: about {rhythms.Average(r => r.BoutsPerHour):F0} pushes an hour, " +
                  $"typically {median:F1}s of climbing heart rate per second of recovery.");

        if (median >= 1.4)
            lines.Add("You play continuously, with little standing around. Good for match conditioning, and it explains why these sessions cost you.");
        else if (median <= 0.8)
            lines.Add("There is more recovery than work in your sessions. Fine for technical practice, but it is not the rhythm of a match.");

        // The spread matters more than the middle: it is the difference between a hit and a battle.
        if (ratios[^1] - ratios[0] >= 0.8)
            lines.Add($"Your sessions vary a lot in how continuous they are, from {ratios[0]:F1} to {ratios[^1]:F1}. Two sessions of equal intensity can still be very different workouts.");
    }

    private static void AppendRecovery(List<string> lines, TrainingLoadReport report)
    {
        var trend = report.RecoveryTrend;
        if (trend.Count < 2) return;

        var first = trend[0].Analysis.HeartRateRecovery60;
        var latest = trend[^1].Analysis.HeartRateRecovery60;
        if (first is not int firstValue || latest is not int latestValue) return;

        var delta = latestValue - firstValue;
        var direction = delta >= MeaningfulBpmChange ? "improving"
            : delta <= -MeaningfulBpmChange ? "sliding"
            : "flat";

        lines.Add($"One-minute heart-rate recovery: {latestValue} bpm latest vs {firstValue} bpm earliest — {direction} across {trend.Count} comparable sessions.");

        if (delta >= MeaningfulBpmChange)
            lines.Add("Your heart is settling faster after hard efforts than it was. That's aerobic fitness moving in the right direction.");
        else if (delta <= -MeaningfulBpmChange)
            lines.Add("Recovery is slower than it was. That usually means accumulated fatigue rather than lost fitness — check whether the easy sessions have quietly disappeared.");
    }

    private static void AppendDrift(List<string> lines, TrainingLoadReport report)
    {
        var drift = MedianDrift(report);
        if (drift is not int value) return;

        if (value >= MeaningfulDriftBpm)
            lines.Add($"Late-session drift: +{value} bpm in the final third. You're paying more for the same tennis late on, so conditioning is the limiter before technique is.");
        else if (value <= -MeaningfulDriftBpm)
            lines.Add($"Late-session drift: {value} bpm in the final third — you're winding down at the end rather than fading.");
        else
            lines.Add($"Late-session drift: {value:+#;-#;0} bpm — you hold your level to the end.");
    }

    /// <summary>Typical bpm rise from the middle to the final third of a tennis session.</summary>
    public static int? MedianDrift(TrainingLoadReport report)
    {
        var drifts = report.TennisWorkouts
            .Where(w => w.Analysis.DriftBpm.HasValue)
            .Select(w => w.Analysis.DriftBpm!.Value)
            .OrderBy(d => d)
            .ToList();

        return drifts.Count > 0 ? drifts[drifts.Count / 2] : null;
    }
}
