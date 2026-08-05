using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public sealed class GoalsModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly InteractionService _interaction;
    private readonly CoachService _coach;

    public GoalsModel(TennisDbContext db, InteractionService interaction, CoachService coach)
    {
        _db = db;
        _interaction = interaction;
        _coach = coach;
    }

    public List<GoalViewModel> ActiveGoals { get; set; } = [];
    public List<GoalViewModel> CompletedGoals { get; set; } = [];

    [BindProperty]
    public string NewGoalName { get; set; } = string.Empty;

    [BindProperty]
    public string NewGoalCategory { get; set; } = GoalCategories.Technique;

    [BindProperty]
    public string? NewGoalDescription { get; set; }

    public async Task OnGetAsync()
    {
        await LoadGoalsAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(NewGoalName))
        {
            TempData["Error"] = "Goal name is required.";
            await LoadGoalsAsync();
            return Page();
        }

        // Soft cap: max 5 active goals
        var activeCount = await _db.DevelopmentGoals.CountAsync(g => g.Status == GoalStatuses.Active, ct);
        if (activeCount >= 5)
        {
            TempData["Error"] = "You can have at most 5 active goals. Complete or archive one first.";
            await LoadGoalsAsync();
            return Page();
        }

        var goal = new DevelopmentGoal
        {
            Name = NewGoalName.Trim(),
            Category = GoalCategories.All.Contains(NewGoalCategory) ? NewGoalCategory : GoalCategories.Technique,
            Description = string.IsNullOrWhiteSpace(NewGoalDescription) ? null : NewGoalDescription.Trim(),
            Status = GoalStatuses.Active,
            CreatedAt = DateTime.UtcNow
        };

        _db.DevelopmentGoals.Add(goal);
        await _db.SaveChangesAsync(ct);
        await _interaction.LogAsync(PageNames.Goals, InteractionActions.GoalCreated, goal.Name);

        TempData["Success"] = $"Goal \"{goal.Name}\" created! 🎯";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken ct)
    {
        var goal = await _db.DevelopmentGoals.FindAsync([id], ct);
        if (goal != null && goal.Status == GoalStatuses.Active)
        {
            goal.Status = GoalStatuses.Completed;
            goal.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _interaction.LogAsync(PageNames.Goals, InteractionActions.GoalCompleted, goal.Name);
            TempData["Success"] = $"🎉 \"{goal.Name}\" marked as completed! Great progress!";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArchiveAsync(int id, CancellationToken ct)
    {
        var goal = await _db.DevelopmentGoals.FindAsync([id], ct);
        if (goal != null)
        {
            goal.Status = GoalStatuses.Archived;
            goal.CompletedAt ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _interaction.LogAsync(PageNames.Goals, InteractionActions.GoalArchived, goal.Name);
            TempData["Success"] = $"\"{goal.Name}\" archived.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetSuggestAsync(CancellationToken ct)
    {
        var sessionCount = await _db.Sessions.CountAsync(ct);
        var activeGoals = await _db.DevelopmentGoals
            .Where(g => g.Status == GoalStatuses.Active)
            .Select(g => g.Name)
            .ToListAsync(ct);

        var prompt = "Based on my session history, suggest 2-3 new development goals I should work on. " +
            "For each suggestion, give: the goal name, the category (Technique/Tactical/Mental/Fitness/Fundamentals), " +
            "and a one-sentence reason why this would help me improve. " +
            "Format each as a bullet point.";

        if (activeGoals.Count > 0)
            prompt += $" My current active goals are: {string.Join(", ", activeGoals)}. Don't suggest duplicates.";

        if (sessionCount == 0)
            prompt += " I haven't logged any sessions yet, so suggest common beginner development goals.";

        try
        {
            var response = await _coach.AskCoachAsync(prompt, ct);
            return new JsonResult(new { suggestions = response });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonResult(new { suggestions = "Couldn't generate suggestions right now. Try again later or check if Ollama is running." });
        }
    }

    private async Task LoadGoalsAsync()
    {
        var goals = await _db.DevelopmentGoals
            .Include(g => g.CheckIns)
            .OrderBy(g => g.Status == GoalStatuses.Active ? 0 : 1)
            .ThenByDescending(g => g.CreatedAt)
            .ToListAsync();

        ActiveGoals = goals
            .Where(g => g.Status == GoalStatuses.Active)
            .Select(ToViewModel)
            .ToList();

        CompletedGoals = goals
            .Where(g => g.Status is GoalStatuses.Completed or GoalStatuses.Archived)
            .Select(ToViewModel)
            .ToList();
    }

    private static GoalViewModel ToViewModel(DevelopmentGoal g)
    {
        var checkIns = g.CheckIns.OrderByDescending(c => c.Id).ToList();
        var last5 = checkIns.Take(5).Select(c => c.HowItFelt).Reverse().ToList();

        return new GoalViewModel
        {
            Id = g.Id,
            Name = g.Name,
            Category = g.Category,
            Description = g.Description,
            Status = g.Status,
            CreatedAt = g.CreatedAt,
            CompletedAt = g.CompletedAt,
            TotalCheckIns = checkIns.Count,
            StruggledCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Struggled),
            OkayCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Okay),
            ClickedCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Clicked),
            Last5Feelings = last5,
            DaysActive = (int)((g.CompletedAt ?? DateTime.UtcNow) - g.CreatedAt).TotalDays
        };
    }
}

public sealed class GoalViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalCheckIns { get; set; }
    public int StruggledCount { get; set; }
    public int OkayCount { get; set; }
    public int ClickedCount { get; set; }
    public List<string> Last5Feelings { get; set; } = [];
    public int DaysActive { get; set; }
}
