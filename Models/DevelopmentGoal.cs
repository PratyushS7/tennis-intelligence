using System.ComponentModel.DataAnnotations;

namespace TennisIntelligence.Models;

public sealed class DevelopmentGoal
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    public string Category { get; set; } = GoalCategories.Technique;

    [MaxLength(200)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = GoalStatuses.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public List<GoalCheckIn> CheckIns { get; set; } = [];
}

public sealed class GoalCheckIn
{
    public int Id { get; set; }

    public int GoalId { get; set; }
    public DevelopmentGoal Goal { get; set; } = null!;

    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string HowItFelt { get; set; } = GoalFeelings.Okay;

    [MaxLength(200)]
    public string? Note { get; set; }
}

public static class GoalCategories
{
    public const string Technique = "Technique";
    public const string Tactical = "Tactical";
    public const string Mental = "Mental";
    public const string Fitness = "Fitness";
    public const string Fundamentals = "Fundamentals";

    public static readonly string[] All = [Technique, Tactical, Mental, Fitness, Fundamentals];
}

public static class GoalStatuses
{
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Archived = "Archived";
}

public static class GoalFeelings
{
    public const string Struggled = "Struggled";
    public const string Okay = "Okay";
    public const string Clicked = "Clicked";

    public static readonly string[] All = [Struggled, Okay, Clicked];

    public static string ToEmoji(string feeling) => feeling switch
    {
        Struggled => "😤",
        Okay => "😐",
        Clicked => "✅",
        _ => "❓"
    };
}

public static class BodyFeelValues
{
    public const string Good = "Good";
    public const string Okay = "Okay";
    public const string Sore = "Sore";

    public static readonly string[] All = [Good, Okay, Sore];

    public static string ToEmoji(string? feel) => feel switch
    {
        Good => "💪",
        Okay => "👌",
        Sore => "🩹",
        _ => "—"
    };
}
