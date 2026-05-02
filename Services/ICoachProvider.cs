namespace TennisIntelligence.Services;

public class SessionContext
{
    public int TotalSessions { get; set; }
    public double AvgEnergy { get; set; }
    public double AvgElbowPain { get; set; }
    public double AvgShoulderTightness { get; set; }
    public Dictionary<string, int> BreakdownAreaCounts { get; set; } = [];
    public Dictionary<string, int> BreakdownReasonCounts { get; set; } = [];
    public int FocusAchievedCount { get; set; }
    public int FocusTotalCount { get; set; }
    public List<SessionSummary> RecentSessions { get; set; } = [];

    // Match context aggregates
    public Dictionary<string, int> PlayStyleCounts { get; set; } = [];
    public Dictionary<string, int> MentalStateCounts { get; set; } = [];
    public Dictionary<string, int> OpponentLevelCounts { get; set; } = [];
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }
    public Dictionary<string, (int Wins, int Losses)> WinRateByOpponentLevel { get; set; } = [];

    // App usage context (for AI feedback loop)
    public UsageSummary? Usage { get; set; }

    // Development goals context (v2)
    public List<GoalSummary> ActiveGoals { get; set; } = [];
    public List<GoalSummary> RecentlyCompleted { get; set; } = [];
}

public class GoalSummary
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int TotalCheckIns { get; set; }
    public int StruggledCount { get; set; }
    public int OkayCount { get; set; }
    public int ClickedCount { get; set; }
    public List<string> Last5Feelings { get; set; } = [];
    public int DaysActive { get; set; }
}

public class SessionSummary
{
    public DateTime Date { get; set; }
    public int DurationMinutes { get; set; }
    public int EnergyLevel { get; set; }
    public int ElbowPain { get; set; }
    public int ShoulderTightness { get; set; }
    public string BreakdownAreas { get; set; } = "";
    public string BreakdownReasons { get; set; } = "";
    public string? FocusArea { get; set; }
    public bool? FocusAchieved { get; set; }
    public string? Notes { get; set; }
    public int? SessionRating { get; set; }
    public string? SessionType { get; set; }
    public string? OpponentLevel { get; set; }
    public string? PlayStyle { get; set; }
    public string? MentalState { get; set; }
    public string? MatchResult { get; set; }
    public string? EnergyBefore { get; set; }
    public string? EnergyAfter { get; set; }
    public string? MatchFormat { get; set; }
}

public class ChatMessage
{
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public interface ICoachProvider
{
    Task<string> GetCoachingAsync(string userMessage, SessionContext context, CancellationToken ct = default);
    Task<string> GetCoachingAsync(string userMessage, SessionContext context, List<ChatMessage> conversationHistory, CancellationToken ct = default);
    bool IsAvailable { get; }
    string ProviderName { get; }
}
