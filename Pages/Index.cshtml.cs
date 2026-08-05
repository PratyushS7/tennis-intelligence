using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public sealed class IndexModel : PageModel
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

    // Goal journey data for home page
    public List<GoalJourney> ActiveGoalJourneys { get; set; } = [];
    public string? PreSessionTip { get; set; }

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

    public async Task OnGetAsync()
    {
        var sessions = await _db.Sessions.OrderByDescending(s => s.Date).ToListAsync();
        TotalSessions = sessions.Count;

        ActiveGoalCount = await _db.DevelopmentGoals.CountAsync(g => g.Status == GoalStatuses.Active);

        if (sessions.Count == 0) return;

        RecentSession = sessions[0];
        RecentSessions = sessions.Take(5).ToList();

        // Days since last session
        var utcToday = DateTime.UtcNow.Date;
        DaysSinceLastSession = (utcToday - RecentSession.Date.Date).Days;

        // This week count
        var weekStart = utcToday.AddDays(-(int)utcToday.DayOfWeek);
        ThisWeekCount = sessions.Count(s => s.Date.Date >= weekStart);

        // Current streak
        CurrentStreak = CalculateStreak(sessions, utcToday);

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

        // Load active goals with check-in timelines for the journal view
        var activeGoals = await _db.DevelopmentGoals
            .Include(g => g.CheckIns)
            .Where(g => g.Status == GoalStatuses.Active)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        ActiveGoalJourneys = activeGoals.Select(g =>
        {
            var checkIns = g.CheckIns.OrderByDescending(c => c.Id).ToList();
            var last5 = checkIns.Take(5).Reverse().ToList();
            var trend = GetGoalTrend(last5);
            return new GoalJourney
            {
                Id = g.Id,
                Name = g.Name,
                Category = g.Category,
                TotalCheckIns = checkIns.Count,
                Last5Emojis = last5.Select(c => GoalFeelings.ToEmoji(c.HowItFelt)).ToList(),
                Trend = trend,
                DaysActive = (int)(DateTime.UtcNow - g.CreatedAt).TotalDays,
                ClickedRate = checkIns.Count > 0
                    ? (int)(100.0 * checkIns.Count(c => c.HowItFelt == GoalFeelings.Clicked) / checkIns.Count)
                    : 0
            };
        }).ToList();
        ActiveGoalCount = ActiveGoalJourneys.Count;

        // Generate pre-session tip based on goals and recent patterns
        PreSessionTip = GeneratePreSessionTip(sessions, ActiveGoalJourneys);
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

    private static int CalculateStreak(List<Session> sessions, DateTime today)
    {
        if (sessions.Count == 0) return 0;

        var dates = sessions.Select(s => s.Date.Date).Distinct().OrderByDescending(d => d).ToList();

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
        var utcToday = DateTime.UtcNow.Date;
        var session = new Session
        {
            Date = utcToday,
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
        await _db.SaveChangesAsync();

        await _interaction.LogAsync(PageNames.Home, InteractionActions.SessionLogged, "quick-log");

        TempData["Success"] = "Quick session logged! 🎾⚡";
        TempData["ShowDebrief"] = true;
        return RedirectToPage();
    }

    private static string GetGoalTrend(List<GoalCheckIn> last5)
    {
        if (last5.Count < 2) return "🆕";
        var recent = last5.TakeLast(2).ToList();
        var feelings = new Dictionary<string, int>
        {
            [GoalFeelings.Struggled] = 0,
            [GoalFeelings.Okay] = 1,
            [GoalFeelings.Clicked] = 2
        };
        if (!feelings.TryGetValue(recent[1].HowItFelt, out var latest) ||
            !feelings.TryGetValue(recent[0].HowItFelt, out var previous))
            return "➡️";

        if (latest > previous) return "📈";
        if (latest < previous) return "📉";
        return "➡️";
    }

    private static string? GeneratePreSessionTip(List<Session> sessions, List<GoalJourney> goals)
    {
        if (goals.Count == 0 && sessions.Count == 0) return null;

        // Find a goal that's been struggling recently
        var strugglingGoal = goals.FirstOrDefault(g =>
            g.Last5Emojis.Count > 0 && g.Last5Emojis.Last() == GoalFeelings.ToEmoji(GoalFeelings.Struggled));
        if (strugglingGoal != null)
            return $"💡 Your **{strugglingGoal.Name}** felt tough last time. Try focusing on just that one thing today — keep it simple.";

        // Find goal with most momentum (clicking)
        var clickingGoal = goals.FirstOrDefault(g =>
            g.Last5Emojis.Count >= 2 && g.Last5Emojis.TakeLast(2).All(e => e == GoalFeelings.ToEmoji(GoalFeelings.Clicked)));
        if (clickingGoal != null)
            return $"🔥 **{clickingGoal.Name}** has been clicking! Keep the momentum — maybe push it in a match situation today.";

        // Suggest focus on newest goal with no check-ins
        var newGoal = goals.FirstOrDefault(g => g.TotalCheckIns == 0);
        if (newGoal != null)
            return $"🎯 You added **{newGoal.Name}** but haven't checked in yet. Make it your focus today!";

        // Default: pick the top breakdown area
        if (sessions.Count > 0)
        {
            var topBreakdown = sessions.Take(5)
                .SelectMany(s => s.BreakdownAreaList)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .GroupBy(a => a.Trim())
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
            if (topBreakdown != null)
                return $"💡 **{topBreakdown}** has been breaking down recently. Spend 10 minutes warming it up before you play.";
        }

        return goals.Count > 0
            ? $"🎾 You have {goals.Count} active goal(s). Pick one to focus on today!"
            : null;
    }
}

public sealed class GoalJourney
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int TotalCheckIns { get; set; }
    public List<string> Last5Emojis { get; set; } = [];
    public string Trend { get; set; } = "";
    public int DaysActive { get; set; }
    public int ClickedRate { get; set; }
}
