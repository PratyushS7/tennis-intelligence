using System.ComponentModel.DataAnnotations;

namespace TennisIntelligence.Models;

public class InteractionLog
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PageName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Metadata { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class PageNames
{
    public const string Home = "Home";
    public const string LogSession = "LogSession";
    public const string History = "History";
    public const string Insights = "Insights";
    public const string Coach = "Coach";
    public const string Goals = "Goals";
    public const string GoalDetail = "GoalDetail";
}

public static class InteractionActions
{
    // Automatic (logged by filter)
    public const string PageView = "PageView";

    // Manual (logged in page models)
    public const string SessionLogged = "SessionLogged";
    public const string SessionDeleted = "SessionDeleted";
    public const string CoachAsked = "CoachAsked";
    public const string QuickPromptUsed = "QuickPromptUsed";
    public const string ChatCleared = "ChatCleared";
    public const string InsightRequested = "InsightRequested";

    // Goal actions (v2)
    public const string GoalCreated = "GoalCreated";
    public const string GoalCompleted = "GoalCompleted";
    public const string GoalArchived = "GoalArchived";
    public const string GoalCheckInLogged = "GoalCheckInLogged";
}
