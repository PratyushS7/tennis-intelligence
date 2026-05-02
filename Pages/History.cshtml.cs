using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public class HistoryModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly InteractionService _interaction;

    public HistoryModel(TennisDbContext db, InteractionService interaction)
    {
        _db = db;
        _interaction = interaction;
    }

    public List<Session> Sessions { get; set; } = [];

    public void OnGet()
    {
        Sessions = _db.Sessions
            .OrderByDescending(s => s.Date)
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var session = _db.Sessions.Find(id);
        if (session != null)
        {
            _db.Sessions.Remove(session);
            _db.SaveChanges();
            await _interaction.LogAsync(PageNames.History, InteractionActions.SessionDeleted);
            TempData["Success"] = "Session deleted.";
        }
        return RedirectToPage();
    }
}
