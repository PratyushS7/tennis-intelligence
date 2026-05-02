using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

public class CoachService
{
    private readonly TennisDbContext _db;
    private readonly OllamaCoachProvider _ollama;
    private readonly RuleBasedCoachProvider _ruleBased;
    private readonly InteractionService _interaction;

    public CoachService(TennisDbContext db, OllamaCoachProvider ollama, RuleBasedCoachProvider ruleBased, InteractionService interaction)
    {
        _db = db;
        _ollama = ollama;
        _ruleBased = ruleBased;
        _interaction = interaction;
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
        var context = await BuildContextAsync();
        ICoachProvider provider = _ollama.IsAvailable ? _ollama : _ruleBased;

        try
        {
            return await provider.GetCoachingAsync(userMessage, context, conversationHistory, ct);
        }
        catch
        {
            return await _ruleBased.GetCoachingAsync(userMessage, context, conversationHistory, ct);
        }
    }

    private async Task<SessionContext> BuildContextAsync()
    {
        var sessions = _db.Sessions
            .OrderByDescending(s => s.Date)
            .ToList();

        if (sessions.Count == 0)
            return new SessionContext();

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
        catch
        {
            // Don't let interaction tracking failures break coaching
        }

        // Enrich with goal data (v2)
        try
        {
            var goals = await _db.DevelopmentGoals
                .Include(g => g.CheckIns)
                .ToListAsync();

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
        catch
        {
            // Don't let goal loading failures break coaching
        }

        return ctx;
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
