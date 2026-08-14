using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public sealed class InsightsModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly TrainingLoadService _trainingLoad;

    public InsightsModel(TennisDbContext db, TrainingLoadService trainingLoad)
    {
        _db = db;
        _trainingLoad = trainingLoad;
    }

    /// <summary>Wearable-derived picture, which unlike the manual log needs nothing entered by hand.</summary>
    public TrainingLoadReport TrainingLoad { get; set; } = new();

    public int TotalSessions { get; set; }
    public double AverageEnergy { get; set; }
    public double AverageElbowPain { get; set; }
    public double AverageShoulderTightness { get; set; }
    public int TotalMinutes { get; set; }

    public int FocusTotal { get; set; }
    public int FocusAchievedCount { get; set; }
    public int FocusAchievedPct { get; set; }

    public Dictionary<string, int> BreakdownAreaCounts { get; set; } = [];
    public Dictionary<string, int> BreakdownReasonCounts { get; set; } = [];
    public List<WeeklySummary> WeeklySummaries { get; set; } = [];

    public List<Session> BestSessions { get; set; } = [];
    public string BestSessionCommonFactors { get; set; } = string.Empty;
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        TrainingLoad = await _trainingLoad.GetReportAsync(ct);

        var sessions = await _db.Sessions.ToListAsync(ct);
        TotalSessions = sessions.Count;

        if (TotalSessions == 0) return;

        AverageEnergy = sessions.Average(s => s.EnergyLevel);
        AverageElbowPain = sessions.Where(s => s.ElbowPain.HasValue).Select(s => (double)s.ElbowPain!.Value).DefaultIfEmpty(0).Average();
        AverageShoulderTightness = sessions.Where(s => s.ShoulderTightness.HasValue).Select(s => (double)s.ShoulderTightness!.Value).DefaultIfEmpty(0).Average();
        TotalMinutes = sessions.Sum(s => s.DurationMinutes);

        // Focus tracking
        var focusSessions = sessions.Where(s => s.FocusAchieved.HasValue).ToList();
        FocusTotal = focusSessions.Count;
        FocusAchievedCount = focusSessions.Count(s => s.FocusAchieved == true);
        FocusAchievedPct = FocusTotal > 0 ? (int)(100.0 * FocusAchievedCount / FocusTotal) : 0;

        // Breakdown areas - count frequency
        BreakdownAreaCounts = sessions
            .SelectMany(s => s.BreakdownAreaList)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a.Trim())
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        // Breakdown reasons - count frequency
        BreakdownReasonCounts = sessions
            .SelectMany(s => s.BreakdownReasonList)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .GroupBy(r => r.Trim())
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        // Weekly summaries (last 4 weeks)
        var fourWeeksAgo = DateTime.UtcNow.Date.AddDays(-28);
        WeeklySummaries = sessions
            .Where(s => s.Date >= fourWeeksAgo)
            .GroupBy(s => StartOfWeek(s.Date))
            .OrderByDescending(g => g.Key)
            .Select(g => new WeeklySummary
            {
                WeekStart = g.Key,
                Count = g.Count(),
                AvgEnergy = g.Average(s => s.EnergyLevel),
                AvgElbow = g.Where(s => s.ElbowPain.HasValue).Select(s => (double)s.ElbowPain!.Value).DefaultIfEmpty(0).Average(),
                AvgShoulder = g.Where(s => s.ShoulderTightness.HasValue).Select(s => (double)s.ShoulderTightness!.Value).DefaultIfEmpty(0).Average(),
                TotalMinutes = g.Sum(s => s.DurationMinutes)
            })
            .ToList();

        // Best sessions (rating 4-5)
        BestSessions = sessions
            .Where(s => s.SessionRating.HasValue && s.SessionRating >= 4)
            .OrderByDescending(s => s.SessionRating)
            .ThenByDescending(s => s.Date)
            .Take(5)
            .ToList();

        BestSessionCommonFactors = ComputeBestSessionFactors(BestSessions, sessions);

        // Streak calculations
        var sortedDates = sessions.Select(s => s.Date.Date).Distinct().OrderBy(d => d).ToList();
        var utcToday = DateTime.UtcNow.Date;
        CurrentStreak = CalculateCurrentStreak(sortedDates, utcToday);
        LongestStreak = CalculateLongestStreak(sortedDates);
    }

    private static string ComputeBestSessionFactors(List<Session> best, List<Session> all)
    {
        if (best.Count == 0) return "Not enough rated sessions yet.";

        var factors = new List<string>();

        var avgEnergy = best.Average(s => s.EnergyLevel);
        factors.Add($"High energy (avg {avgEnergy:F1})");

        // Find breakdown areas that appear across all sessions but NOT in best sessions
        var allAreas = all.SelectMany(s => s.BreakdownAreaList).Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
        var bestAreas = best.SelectMany(s => s.BreakdownAreaList).Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToHashSet();
        var absentAreas = allAreas.Where(a => !bestAreas.Contains(a)).ToList();
        if (absentAreas.Count > 0)
            factors.Add($"no {string.Join(", ", absentAreas.Take(3))} breakdowns");

        return string.Join(", ", factors);
    }

    /// <summary>Consecutive play days from today, allowing a 1-day gap.</summary>
    private static int CalculateCurrentStreak(List<DateTime> sortedDates, DateTime today)
    {
        if (sortedDates.Count == 0) return 0;

        int streak = 1;
        var cursor = today;

        // Find the most recent session date on or before today
        var lastIdx = sortedDates.FindLastIndex(d => d <= cursor);
        if (lastIdx < 0) return 0;

        // Allow starting if last session was today or yesterday (1-day gap tolerance)
        var gap = (cursor - sortedDates[lastIdx]).Days;
        if (gap > 1) return 0;

        for (int i = lastIdx; i > 0; i--)
        {
            var diff = (sortedDates[i] - sortedDates[i - 1]).Days;
            if (diff <= 2) // allow 1-day gap
                streak++;
            else
                break;
        }
        return streak;
    }

    /// <summary>Longest streak ever, allowing a 1-day gap between sessions.</summary>
    private static int CalculateLongestStreak(List<DateTime> sortedDates)
    {
        if (sortedDates.Count == 0) return 0;

        int longest = 1, current = 1;
        for (int i = 1; i < sortedDates.Count; i++)
        {
            var diff = (sortedDates[i] - sortedDates[i - 1]).Days;
            if (diff <= 2)
                current++;
            else
                current = 1;
            if (current > longest) longest = current;
        }
        return longest;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}

public sealed class WeeklySummary
{
    public DateTime WeekStart { get; set; }
    public int Count { get; set; }
    public double AvgEnergy { get; set; }
    public double AvgElbow { get; set; }
    public double AvgShoulder { get; set; }
    public int TotalMinutes { get; set; }
}
