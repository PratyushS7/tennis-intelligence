using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public class GoalDetailModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly InteractionService _interaction;

    public GoalDetailModel(TennisDbContext db, InteractionService interaction)
    {
        _db = db;
        _interaction = interaction;
    }

    public DevelopmentGoal Goal { get; set; } = null!;
    public List<CheckInRow> CheckInHistory { get; set; } = [];
    public int StruggledCount { get; set; }
    public int OkayCount { get; set; }
    public int ClickedCount { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var goal = await _db.DevelopmentGoals
            .Include(g => g.CheckIns)
                .ThenInclude(c => c.Session)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (goal == null)
            return RedirectToPage("/Goals");

        Goal = goal;

        var checkIns = goal.CheckIns.OrderByDescending(c => c.Session.Date).ToList();
        StruggledCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Struggled);
        OkayCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Okay);
        ClickedCount = checkIns.Count(c => c.HowItFelt == GoalFeelings.Clicked);

        CheckInHistory = checkIns.Select(c => new CheckInRow
        {
            Date = c.Session.Date,
            SessionRating = c.Session.SessionRating,
            HowItFelt = c.HowItFelt,
            Note = c.Note,
            SessionType = c.Session.SessionType
        }).ToList();

        await _interaction.LogAsync(PageNames.GoalDetail, InteractionActions.PageView, goal.Name);

        return Page();
    }
}

public class CheckInRow
{
    public DateTime Date { get; set; }
    public int? SessionRating { get; set; }
    public string HowItFelt { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? SessionType { get; set; }
}
