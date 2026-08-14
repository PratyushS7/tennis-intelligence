using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

public sealed class CoachService
{
    private readonly TennisDbContext _db;
    private readonly OllamaCoachProvider _ollama;
    private readonly RuleBasedCoachProvider _ruleBased;
    private readonly InteractionService _interaction;
    private readonly TrainingLoadService _trainingLoad;
    private readonly ILogger<CoachService> _logger;

    public CoachService(TennisDbContext db, OllamaCoachProvider ollama, RuleBasedCoachProvider ruleBased, InteractionService interaction, TrainingLoadService trainingLoad, ILogger<CoachService> logger)
    {
        _db = db;
        _ollama = ollama;
        _ruleBased = ruleBased;
        _interaction = interaction;
        _trainingLoad = trainingLoad;
        _logger = logger;
    }

    public string ActiveProviderName
    {
        get
        {
            try { return _ollama.IsAvailable ? _ollama.ProviderName : _ruleBased.ProviderName; }
            catch { return _ruleBased.ProviderName; }
        }
    }

    public async Task<string> AskCoachAsync(string userMessage, CancellationToken ct = default)
        => await AskCoachAsync(userMessage, [], ct);

    public async Task<string> AskCoachAsync(string userMessage, List<ChatMessage> conversationHistory, CancellationToken ct = default)
    {
        var context = await BuildContextAsync(ct);
        ICoachProvider provider = _ollama.IsAvailable ? _ollama : _ruleBased;

        try
        {
            return await provider.GetCoachingAsync(userMessage, context, conversationHistory, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Primary coach provider {Provider} failed, falling back to rule-based", provider.ProviderName);
            return await _ruleBased.GetCoachingAsync(userMessage, context, conversationHistory, ct);
        }
    }

    private async Task<SessionContext> BuildContextAsync(CancellationToken ct = default)
    {
        // Limit to last 100 sessions for aggregates to avoid unbounded memory growth
        var sessions = await _db.Sessions
            .OrderByDescending(s => s.Date)
            .Take(100)
            .ToListAsync(ct);

        if (sessions.Count == 0)
        {
            var wearableOnlyContext = new SessionContext();
            await EnrichWithWearableDataAsync(wearableOnlyContext, ct);
            return wearableOnlyContext;
        }

        var ctx = new SessionContext
        {
            TotalSessions = sessions.Count,
            AvgEnergy = sessions.Average(s => s.EnergyLevel),
            AvgElbowPain = sessions.Where(s => s.ElbowPain.HasValue).Select(s => (double)s.ElbowPain!.Value).DefaultIfEmpty(0).Average(),
            AvgShoulderTightness = sessions.Where(s => s.ShoulderTightness.HasValue).Select(s => (double)s.ShoulderTightness!.Value).DefaultIfEmpty(0).Average(),
        };

        // Focus stats
        var focused = sessions.Where(s => s.FocusAchieved.HasValue).ToList();
        ctx.FocusTotalCount = focused.Count;
        ctx.FocusAchievedCount = focused.Count(s => s.FocusAchieved == true);

        // Breakdown counts
        ctx.BreakdownAreaCounts = sessions
            .SelectMany(s => s.BreakdownAreaList)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        ctx.BreakdownReasonCounts = sessions
            .SelectMany(s => s.BreakdownReasonList)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .GroupBy(r => r.Trim())
            .ToDictionary(g => g.Key, g => g.Count());

        // Match context aggregates
        ctx.PlayStyleCounts = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.PlayStyle))
            .GroupBy(s => s.PlayStyle!)
            .ToDictionary(g => g.Key, g => g.Count());

        ctx.MentalStateCounts = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.MentalState))
            .GroupBy(s => s.MentalState!)
            .ToDictionary(g => g.Key, g => g.Count());

        ctx.OpponentLevelCounts = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.OpponentLevel))
            .GroupBy(s => s.OpponentLevel!)
            .ToDictionary(g => g.Key, g => g.Count());

        ctx.MatchesWon = sessions.Count(s => s.MatchResult == "Won");
        ctx.MatchesLost = sessions.Count(s => s.MatchResult == "Lost");

        ctx.WinRateByOpponentLevel = sessions
            .Where(s => !string.IsNullOrWhiteSpace(s.OpponentLevel)
                     && s.MatchResult is "Won" or "Lost")
            .GroupBy(s => s.OpponentLevel!)
            .ToDictionary(
                g => g.Key,
                g => (Wins: g.Count(s => s.MatchResult == "Won"),
                      Losses: g.Count(s => s.MatchResult == "Lost")));

        // Recent sessions
        ctx.RecentSessions = sessions.Take(10).Select(s => new SessionSummary
        {
            Date = s.Date,
            DurationMinutes = s.DurationMinutes,
            EnergyLevel = s.EnergyLevel,
            ElbowPain = s.ElbowPain ?? 0,
            ShoulderTightness = s.ShoulderTightness ?? 0,
            BreakdownAreas = s.BreakdownAreas,
            BreakdownReasons = s.BreakdownReasons,
            FocusArea = s.FocusArea,
            FocusAchieved = s.FocusAchieved,
            Notes = s.Notes,
            SessionRating = s.SessionRating,
            SessionType = s.SessionType,
            OpponentLevel = s.OpponentLevel,
            PlayStyle = s.PlayStyle,
            MentalState = s.MentalState,
            MatchResult = s.MatchResult,
            EnergyBefore = s.EnergyBefore,
            EnergyAfter = s.EnergyAfter,
            MatchFormat = s.MatchFormat,
        }).ToList();

        // Enrich with app usage data
        try
        {
            ctx.Usage = await _interaction.GetUsageSummaryAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load interaction usage data for coaching context");
        }

        // Enrich with goal data (v2)
        try
        {
            var goals = await _db.DevelopmentGoals
                .Include(g => g.CheckIns)
                .ToListAsync(ct);

            ctx.ActiveGoals = goals
                .Where(g => g.Status == GoalStatuses.Active)
                .Select(ToGoalSummary)
                .ToList();

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            ctx.RecentlyCompleted = goals
                .Where(g => g.Status == GoalStatuses.Completed && g.CompletedAt >= thirtyDaysAgo)
                .Select(ToGoalSummary)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load goal data for coaching context");
        }

        await EnrichWithWearableDataAsync(ctx, ct);
        return ctx;
    }

    private async Task EnrichWithWearableDataAsync(SessionContext context, CancellationToken ct)
    {
        try
        {
            context.RecentWearableDays = await _db.ExternalDailySummaries
                .AsNoTracking()
                .OrderByDescending(summary => summary.SummaryDate)
                .Take(14)
                .Select(summary => new WearableDaySummary
                {
                    Date = summary.SummaryDate,
                    Steps = summary.Steps,
                    ActiveCaloriesKcal = summary.ActiveCaloriesKcal,
                    RestingHeartRateBpm = summary.RestingHeartRateBpm,
                    HeartRateVariabilityRmssdMs = summary.HeartRateVariabilityRmssdMs,
                    OxygenSaturationPercent = summary.OxygenSaturationPercent,
                    Vo2MaxMlPerKgPerMin = summary.Vo2MaxMlPerKgPerMin,
                    SleepDurationMinutes = summary.SleepDurationMinutes,
                    DeepSleepMinutes = summary.DeepSleepMinutes,
                    RemSleepMinutes = summary.RemSleepMinutes
                })
                .ToListAsync(ct);

            var report = await _trainingLoad.GetReportAsync(ct);
            var analysisById = report.Workouts.ToDictionary(w => w.Id);

            var recent = await _db.ExternalWorkouts
                .AsNoTracking()
                .OrderByDescending(workout => workout.StartedAt)
                .Take(10)
                .Select(workout => new
                {
                    workout.Id,
                    workout.StartedAt,
                    workout.EndedAt,
                    workout.ActivityType,
                    workout.CaloriesKcal,
                    workout.AverageHeartRateBpm,
                    workout.MaxHeartRateBpm
                })
                .ToListAsync(ct);

            context.RecentWearableWorkouts = recent
                .Select(workout =>
                {
                    var summary = new WearableWorkoutSummary
                    {
                        StartedAt = workout.StartedAt,
                        ActivityType = workout.ActivityType,
                        DurationMinutes = (int)(workout.EndedAt - workout.StartedAt).TotalMinutes,
                        CaloriesKcal = workout.CaloriesKcal,
                        AverageHeartRateBpm = workout.AverageHeartRateBpm,
                        MaxHeartRateBpm = workout.MaxHeartRateBpm
                    };

                    // Only sessions the watch sampled carry an analysis; the rest stay bare rather than guessed at.
                    if (analysisById.TryGetValue(workout.Id, out var analysed) && analysed.Analysis.HasSeries)
                    {
                        summary.Character = analysed.Analysis.Character.ToString();
                        summary.HardZonePct = analysed.Analysis.Zones.HardPct;
                        summary.HeartRateRecovery60 = analysed.Analysis.HeartRateRecovery60;
                        summary.DriftBpm = analysed.Analysis.DriftBpm;
                    }

                    return summary;
                })
                .ToList();

            context.TrainingLoad = BuildTrainingLoad(report);
            context.TrainingLoadLines = TrainingLoadNarrative.Describe(report);

            var latestWeight = await _db.ExternalBodyMeasurements
                .AsNoTracking()
                .Where(measurement => measurement.WeightKg.HasValue)
                .OrderByDescending(measurement => measurement.MeasuredAt)
                .Select(measurement => measurement.WeightKg)
                .FirstOrDefaultAsync(ct);
            var latestBodyFat = await _db.ExternalBodyMeasurements
                .AsNoTracking()
                .Where(measurement => measurement.BodyFatPercent.HasValue)
                .OrderByDescending(measurement => measurement.MeasuredAt)
                .Select(measurement => measurement.BodyFatPercent)
                .FirstOrDefaultAsync(ct);

            context.LatestWeightKg = latestWeight;
            context.LatestBodyFatPercent = latestBodyFat;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load wearable data for coaching context");
        }
    }

    private static WearableTrainingLoad? BuildTrainingLoad(TrainingLoadReport report)
    {
        if (!report.HasData) return null;

        var tennis = report.TennisWorkouts;

        return new WearableTrainingLoad
        {
            ObservedMaxHeartRate = report.ObservedMaxHeartRate,
            TennisSessionsAnalysed = tennis.Count,
            HardSessions = report.TennisHardSessions,
            ModerateSessions = report.TennisModerateSessions,
            LightSessions = report.TennisLightSessions,
            TennisZones = report.TennisZones,
            RecoveryFirst = report.RecoveryTrend.FirstOrDefault()?.Analysis.HeartRateRecovery60,
            RecoveryLatest = report.RecoveryTrend.LastOrDefault()?.Analysis.HeartRateRecovery60,
            RecoveryPoints = report.RecoveryTrend.Count,
            MedianTennisDriftBpm = TrainingLoadNarrative.MedianDrift(report)
        };
    }

    private static GoalSummary ToGoalSummary(DevelopmentGoal g)
    {
        var checkIns = g.CheckIns.OrderByDescending(c => c.Id).ToList();
        return new GoalSummary
        {
            Name = g.Name,
            Category = g.Category,
            TotalCheckIns = checkIns.Count,
            StruggledCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Struggled),
            OkayCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Okay),
            ClickedCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Clicked),
            Last5Feelings = checkIns.Take(5).Select(c => c.HowItFelt).Reverse().ToList(),
            DaysActive = (int)((g.CompletedAt ?? DateTime.UtcNow) - g.CreatedAt).TotalDays
        };
    }
}
