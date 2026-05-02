using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public class CoachModel : PageModel
{
    private readonly CoachService _coach;
    private readonly InteractionService _interaction;
    private const string ChatHistoryKey = "CoachChatHistory";

    public CoachModel(CoachService coach, InteractionService interaction)
    {
        _coach = coach;
        _interaction = interaction;
    }

    [BindProperty]
    public string? UserMessage { get; set; }

    public List<ChatMessage> ChatHistory { get; set; } = [];
    public string ProviderName => _coach.ActiveProviderName;

    public void OnGet()
    {
        ChatHistory = LoadChatHistory();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserMessage))
            return Page();

        ChatHistory = LoadChatHistory();
        var response = await _coach.AskCoachAsync(UserMessage, ChatHistory, ct);

        ChatHistory.Add(new ChatMessage { Role = "user", Content = UserMessage });
        ChatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
        SaveChatHistory(ChatHistory);

        await _interaction.LogAsync(PageNames.Coach, InteractionActions.CoachAsked);

        UserMessage = null;
        return Page();
    }

    public async Task<IActionResult> OnPostQuickAsync(string prompt, CancellationToken ct)
    {
        UserMessage = prompt;
        ChatHistory = LoadChatHistory();
        var response = await _coach.AskCoachAsync(prompt, ChatHistory, ct);

        ChatHistory.Add(new ChatMessage { Role = "user", Content = prompt });
        ChatHistory.Add(new ChatMessage { Role = "assistant", Content = response });
        SaveChatHistory(ChatHistory);

        await _interaction.LogAsync(PageNames.Coach, InteractionActions.QuickPromptUsed, prompt);

        UserMessage = null;
        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        TempData.Remove(ChatHistoryKey);
        ChatHistory = [];
        await _interaction.LogAsync(PageNames.Coach, InteractionActions.ChatCleared);
        return Page();
    }

    private List<ChatMessage> LoadChatHistory()
    {
        if (TempData.Peek(ChatHistoryKey) is string json)
        {
            return JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? [];
        }
        return [];
    }

    private void SaveChatHistory(List<ChatMessage> history)
    {
        // Keep last 20 messages (10 exchanges) to avoid TempData bloat
        if (history.Count > 20)
            history = history.Skip(history.Count - 20).ToList();

        TempData[ChatHistoryKey] = JsonSerializer.Serialize(history);
    }
}
