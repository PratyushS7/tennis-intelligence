using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public class IndexModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly InteractionService _interaction;

    public IndexModel(TennisDbContext db, InteractionService interaction)
    {
        _db = db;
        _interaction = interaction;
    }

    public Session? RecentSession { get; set; }
    public int TotalSessions { get; set; }
    public int CurrentStreak { get; set; }
    public int ThisWeekCount { get; set; }
    public int WeeklyGoal { get; set; } = 3;
    public double AvgEnergy { get; set; }
    public double AvgElbowPain { get; set; }
    public double AvgShoulderTightness { get; set; }
    public int FocusAchievedCount { get; set; }
    public int FocusTotalCount { get; set; }
    public string TopBreakdownArea { get; set; } = "";
    public int ActiveGoalCount { get; set; }
    public List<Session> RecentSessions { get; set; } = [];
    public string MotivationalMessage { get; set; } = "";
    public string? MilestoneMessage { get; set; }
    public int DaysSinceLastSession { get; set; }
    public string EnergyTrend { get; set; } = "";

    // Chart data (last 10 sessions)
    public string ChartLabels { get; set; } = "[]";
    public string ChartEnergy { get; set; } = "[]";
    public string ChartElbow { get; set; } = "[]";
    public string ChartShoulder { get; set; } = "[]";

    // Quick-log properties
    [BindProperty]
    public int QuickDuration { get; set; } = 60;
    [BindProperty]
    public int QuickRating { get; set; }
    [BindProperty]
    public string? QuickEnergyBefore { get; set; }
    [BindProperty]
    public List<string> QuickBreakdowns { get; set; } = [];

    public List<string> BreakdownAreaOptions =>
        ["Forehand", "Backhand", "Serve", "Footwork", "Fitness"];

    public void OnGet()
    {
        var sessions = _db.Sessions.OrderByDescending(s => s.Date).ToList();
        TotalSessions = sessions.Count;

        ActiveGoalCount = _db.DevelopmentGoals.Count(g => g.Status == "Active");

        if (sessions.Count == 0) return;

        RecentSession = sessions.First();
        RecentSessions = sessions.Take(5).ToList();

        // Days since last session
        DaysSinceLastSession = (DateTime.Today - RecentSession.Date.Date).Days;

        // This week count
        var weekStart = DateTime.SpecifyKind(
            DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek), DateTimeKind.Utc);
        ThisWeekCount = sessions.Count(s => s.Date >= weekStart);

        // Current streak
        CurrentStreak = CalculateStreak(sessions);

        // Averages
        AvgEnergy = sessions.Average(s => s.EnergyLevel);
        AvgElbowPain = sessions.Where(s => s.ElbowPain.HasValue).Select(s => (double)s.ElbowPain!.Value).DefaultIfEmpty(0).Average();
        AvgShoulderTightness = sessions.Where(s => s.ShoulderTightness.HasValue).Select(s => (double)s.ShoulderTightness!.Value).DefaultIfEmpty(0).Average();

        // Focus stats
        var focused = sessions.Where(s => s.FocusAchieved.HasValue).ToList();
        FocusTotalCount = focused.Count;
        FocusAchievedCount = focused.Count(s => s.FocusAchieved == true);

        // Top breakdown area
        TopBreakdownArea = sessions
            .SelectMany(s => s.BreakdownAreaList)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a.Trim())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "None yet";

        // Energy trend (last 3 sessions)
        EnergyTrend = GetEnergyTrend(sessions);

        // Motivational message
        MotivationalMessage = GetMotivationalMessage();

        // Milestone check
        MilestoneMessage = GetMilestone(TotalSessions);

        // Chart data (last 10 sessions, oldest first for chart)
        var chartSessions = sessions.Take(10).Reverse().ToList();
        ChartLabels = "[" + string.Join(",", chartSessions.Select(s => $"\"{s.Date:MMM dd}\"")) + "]";
        ChartEnergy = "[" + string.Join(",", chartSessions.Select(s => s.EnergyLevel)) + "]";
        ChartElbow = "[" + string.Join(",", chartSessions.Select(s => s.ElbowPain?.ToString() ?? "null")) + "]";
        ChartShoulder = "[" + string.Join(",", chartSessions.Select(s => s.ShoulderTightness?.ToString() ?? "null")) + "]";
    }

    private string GetMotivationalMessage()
    {
        if (CurrentStreak >= 5)
            return $"🔥 {CurrentStreak}-session streak! You're on fire!";
        if (CurrentStreak >= 3)
            return $"💪 {CurrentStreak} sessions in a row — keep the momentum!";
        if (ThisWeekCount >= WeeklyGoal)
            return "🎉 Weekly goal crushed! You're putting in the work!";
        if (DaysSinceLastSession == 0)
            return "🎾 Great session today! How are you feeling?";
        if (DaysSinceLastSession == 1)
            return "👋 Played yesterday — rest day or ready to go again?";
        if (DaysSinceLastSession <= 3)
            return $"🎾 {DaysSinceLastSession} days since your last hit — get back out there!";
        if (DaysSinceLastSession <= 7)
            return $"⏰ It's been {DaysSinceLastSession} days — your racket misses you!";
        return $"👀 {DaysSinceLastSession} days off — time to shake off the rust!";
    }

    private static string? GetMilestone(int total)
    {
        return total switch
        {
            5 => "🏅 5 sessions logged! You're building a habit!",
            10 => "🎉 Double digits! 10 sessions tracked!",
            25 => "🏆 25 sessions! You're getting serious!",
            50 => "🌟 50 sessions! Half-century milestone!",
            100 => "💯 100 sessions! Legendary commitment!",
            _ => null
        };
    }

    private static string GetEnergyTrend(List<Session> sessions)
    {
        if (sessions.Count < 3) return "";
        var recent3 = sessions.Take(3).ToList();
        var avgRecent = recent3.Average(s => s.EnergyLevel);
        var older = sessions.Skip(3).Take(5).ToList();
        if (older.Count == 0) return "";
        var avgOlder = older.Average(s => s.EnergyLevel);

        if (avgRecent - avgOlder > 1) return "⬆️ Energy trending up!";
        if (avgOlder - avgRecent > 1) return "⬇️ Energy dipping — rest up!";
        return "➡️ Energy holding steady";
    }

    private static int CalculateStreak(List<Session> sessions)
    {
        if (sessions.Count == 0) return 0;

        var dates = sessions.Select(s => s.Date.Date).Distinct().OrderByDescending(d => d).ToList();
        var today = DateTime.Today;

        if (dates[0] != today && dates[0] != today.AddDays(-1))
            return 0;

        int streak = 1;
        for (int i = 1; i < dates.Count; i++)
        {
            var gap = (dates[i - 1] - dates[i]).Days;
            if (gap <= 2)
                streak++;
            else
                break;
        }
        return streak;
    }

    public async Task<IActionResult> OnPostQuickLogAsync()
    {
        var session = new Session
        {
            Date = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc),
            DurationMinutes = QuickDuration > 0 ? QuickDuration : 60,
            EnergyLevel = 5,
            EnergyBefore = QuickEnergyBefore ?? "Normal",
            SessionRating = QuickRating is >= 1 and <= 5 ? QuickRating : null,
            SessionType = "Practice",
            BreakdownAreas = string.Join(",", QuickBreakdowns),
            BreakdownReasons = string.Empty,
            FocusArea = string.Empty,
            Notes = string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.Sessions.Add(session);
        _db.SaveChanges();

        await _interaction.LogAsync(PageNames.Home, InteractionActions.SessionLogged, "quick-log");

        TempData["Success"] = "Quick session logged! 🎾⚡";
        return RedirectToPage();
    }
}
