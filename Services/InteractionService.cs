using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

public sealed class InteractionService
{
    private readonly TennisDbContext _db;

    public InteractionService(TennisDbContext db) => _db = db;

    public async Task LogAsync(string pageName, string action, string? metadata = null)
    {
        _db.InteractionLogs.Add(new InteractionLog
        {
            PageName = pageName,
            Action = action,
            Metadata = metadata,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<UsageSummary> GetUsageSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);

        var recentLogs = await _db.InteractionLogs
            .Where(l => l.Timestamp >= thirtyDaysAgo)
            .ToListAsync();

        var allLogs = recentLogs; // 30-day window is sufficient for summaries

        var summary = new UsageSummary
        {
            TotalInteractions = allLogs.Count,

            PageVisitCounts = allLogs
                .Where(l => l.Action == InteractionActions.PageView)
                .GroupBy(l => l.PageName)
                .ToDictionary(g => g.Key, g => g.Count()),

            CurrentWeekVisitCount = allLogs
                .Count(l => l.Timestamp >= sevenDaysAgo),

            CoachQuestionsAsked = allLogs
                .Count(l => l.Action is InteractionActions.CoachAsked or InteractionActions.QuickPromptUsed),

            MostUsedQuickPrompts = allLogs
                .Where(l => l.Action == InteractionActions.QuickPromptUsed && l.Metadata != null)
                .GroupBy(l => l.Metadata!)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList(),

            InsightsPageViewCount = allLogs
                .Count(l => l.PageName == PageNames.Insights && l.Action == InteractionActions.PageView),
        };

        // Days since last app visit
        var lastVisit = allLogs
            .Where(l => l.Action == InteractionActions.PageView)
            .MaxBy(l => l.Timestamp);
        summary.DaysSinceLastAppVisit = lastVisit != null
            ? (int)(now - lastVisit.Timestamp).TotalDays
            : -1;

        // Days since last session log (from Sessions table, not interaction logs)
        var lastSessionDate = await _db.Sessions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (DateTime?)s.CreatedAt)
            .FirstOrDefaultAsync();
        summary.DaysSinceLastSessionLog = lastSessionDate.HasValue
            ? (int)(now - lastSessionDate.Value).TotalDays
            : -1;

        // Session logging frequency (avg days between session logs, from Sessions table)
        var sessionDates = await _db.Sessions
            .OrderBy(s => s.Date)
            .Select(s => s.Date)
            .ToListAsync();
        if (sessionDates.Count >= 2)
        {
            var totalSpan = (sessionDates[^1] - sessionDates[0]).TotalDays;
            summary.SessionLoggingFrequencyDays = totalSpan / (sessionDates.Count - 1);
        }

        // Field completion rates — use projections instead of loading full entities
        var totalSessionCount = await _db.Sessions.CountAsync();
        if (totalSessionCount > 0)
        {
            summary.FieldCompletionRates = new Dictionary<string, double>
            {
                ["FocusArea"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.FocusArea != null && s.FocusArea != "") / totalSessionCount, 1),
                ["Notes"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.Notes != null && s.Notes != "") / totalSessionCount, 1),
                ["SessionRating"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.SessionRating != null) / totalSessionCount, 1),
                ["OpponentLevel"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.OpponentLevel != null && s.OpponentLevel != "") / totalSessionCount, 1),
                ["MentalState"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.MentalState != null && s.MentalState != "") / totalSessionCount, 1),
                ["MatchResult"] = Math.Round(100.0 * await _db.Sessions.CountAsync(s => s.MatchResult != null && s.MatchResult != "" && s.MatchResult != "N/A") / totalSessionCount, 1),
            };
        }

        return summary;
    }
}

public sealed class UsageSummary
{
    public int TotalInteractions { get; set; }
    public Dictionary<string, int> PageVisitCounts { get; set; } = [];
    public int CurrentWeekVisitCount { get; set; }
    public int DaysSinceLastAppVisit { get; set; } = -1;
    public int DaysSinceLastSessionLog { get; set; } = -1;
    public double SessionLoggingFrequencyDays { get; set; }
    public int CoachQuestionsAsked { get; set; }
    public List<string> MostUsedQuickPrompts { get; set; } = [];
    public int InsightsPageViewCount { get; set; }
    public Dictionary<string, double> FieldCompletionRates { get; set; } = [];
}
