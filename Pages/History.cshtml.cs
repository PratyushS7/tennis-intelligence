using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public sealed class HistoryModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly InteractionService _interaction;

    public HistoryModel(TennisDbContext db, InteractionService interaction)
    {
        _db = db;
        _interaction = interaction;
    }

    public List<Session> Sessions { get; set; } = [];

    public async Task OnGetAsync()
    {
        Sessions = await _db.Sessions
            .OrderByDescending(s => s.Date)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session != null)
        {
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync();
            await _interaction.LogAsync(PageNames.History, InteractionActions.SessionDeleted);
            TempData["Success"] = "Session deleted.";
        }
        return RedirectToPage();
    }
}
