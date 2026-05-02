using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

#pragma warning disable CS8618 // initialized in constructor
public class LogSessionModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly CoachService _coach;
    private readonly InteractionService _interaction;

    public LogSessionModel(TennisDbContext db, CoachService coach, InteractionService interaction)
    {
        _db = db;
        _coach = coach;
        _interaction = interaction;
    }

    [BindProperty]
    public Session Session { get; set; } = new() { Date = DateTime.Today, EnergyLevel = 5 };

    [BindProperty]
    public List<string> SelectedBreakdownAreas { get; set; } = [];

    [BindProperty]
    public List<string> SelectedBreakdownReasons { get; set; } = [];

    // Goal check-in input models
    [BindProperty]
    public List<GoalCheckInInput> GoalCheckIns { get; set; } = [];

    // Active goals to display in the form
    public List<DevelopmentGoal> ActiveGoals { get; set; } = [];

    public List<string> BreakdownAreaOptions =>
        ["Forehand", "Backhand", "Serve", "Volley", "Footwork", "Return", "Overhead", "Fitness"];

    public List<string> BreakdownReasonOptions =>
        ["Late to ball", "Poor timing", "Bad decision", "Low energy", "Lack of focus", "Tight muscles", "Wrong grip", "Poor positioning"];

    public List<string> SessionTypeOptions =>
        ["Practice", "Match", "Drill", "Hitting"];

    public List<string> OpponentLevelOptions => ["Below me", "Similar", "Above me"];
    public List<string> PlayStyleOptions => ["Aggressive", "Defensive", "All-Court", "Counter-Puncher"];
    public List<string> MentalStateOptions => ["Confident", "Neutral", "Frustrated", "Choked"];
    public List<string> MatchResultOptions => ["Won", "Lost", "N/A"];

    public async Task OnGetAsync()
    {
        ActiveGoals = await _db.DevelopmentGoals
            .Where(g => g.Status == GoalStatuses.Active)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        // Pre-populate GoalCheckIns input list
        GoalCheckIns = ActiveGoals.Select(g => new GoalCheckInInput
        {
            GoalId = g.Id,
            GoalName = g.Name,
            WorkedOnIt = false,
            HowItFelt = string.Empty,
            Note = string.Empty
        }).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Reload active goals for display if validation fails
        ActiveGoals = await _db.DevelopmentGoals
            .Where(g => g.Status == GoalStatuses.Active)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();

        if (!ModelState.IsValid)
            return Page();

        Session.BreakdownAreas = string.Join(",", SelectedBreakdownAreas);
        Session.BreakdownReasons = string.Join(",", SelectedBreakdownReasons);
        Session.SessionType ??= "Practice";
        Session.FocusArea ??= string.Empty;
        Session.Notes ??= string.Empty;
        Session.Date = DateTime.SpecifyKind(Session.Date, DateTimeKind.Utc);
        Session.CreatedAt = DateTime.UtcNow;

        _db.Sessions.Add(Session);
        await _db.SaveChangesAsync();

        // Save goal check-ins (only for goals that were worked on)
        var checkInCount = 0;
        foreach (var input in GoalCheckIns)
        {
            if (!input.WorkedOnIt)
                continue;

            // Verify the goal exists and is active
            var goalExists = await _db.DevelopmentGoals.AnyAsync(g => g.Id == input.GoalId && g.Status == GoalStatuses.Active);
            if (!goalExists)
                continue;

            var feeling = GoalFeelings.All.Contains(input.HowItFelt) ? input.HowItFelt : GoalFeelings.Okay;

            _db.GoalCheckIns.Add(new GoalCheckIn
            {
                GoalId = input.GoalId,
                SessionId = Session.Id,
                HowItFelt = feeling,
                Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
            });
            checkInCount++;
        }

        if (checkInCount > 0)
            await _db.SaveChangesAsync();

        await _interaction.LogAsync(PageNames.LogSession, InteractionActions.SessionLogged,
            checkInCount > 0 ? $"goals:{checkInCount}" : null);

        if (checkInCount > 0)
        {
            await _interaction.LogAsync(PageNames.LogSession, InteractionActions.GoalCheckInLogged,
                $"count:{checkInCount}");
        }

        TempData["Success"] = checkInCount > 0
            ? $"Session logged with {checkInCount} goal check-in(s)! 🎾🎯"
            : "Session logged successfully! 🎾";
        TempData["ShowInsight"] = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetInsightAsync()
    {
        try
        {
            await _interaction.LogAsync(PageNames.LogSession, InteractionActions.InsightRequested);
            var tip = await _coach.AskCoachAsync(
                "Give me ONE brief, actionable tip (2-3 sentences max) based on my most recent session and active goals. Be specific and encouraging.",
                HttpContext.RequestAborted);
            return new JsonResult(new { tip });
        }
        catch
        {
            return new JsonResult(new { tip = "Great job logging your session! Keep tracking to unlock personalized insights. 🎾" });
        }
    }
}

public class GoalCheckInInput
{
    public int GoalId { get; set; }
    public string GoalName { get; set; } = string.Empty;
    public bool WorkedOnIt { get; set; }
    public string HowItFelt { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
