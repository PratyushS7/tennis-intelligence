using System.ComponentModel.DataAnnotations;

namespace TennisIntelligence.Models;

public sealed class Session
{
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, 600)]
    public int DurationMinutes { get; set; }

    [Range(0, 10)]
    public int EnergyLevel { get; set; } = 5;

    // Before/After energy: Fresh, Normal, Tired / Still had gas, Just right, Completely gassed
    public string? EnergyBefore { get; set; }
    public string? EnergyAfter { get; set; }

    // Match format: Singles, Doubles
    public string? MatchFormat { get; set; }

    // Body feel (v2 simplified body check)
    public string? BodyFeel { get; set; }  // Good, Okay, Sore

    // Legacy pain tracking (nullable — v2 deprioritizes these)
    [Range(1, 10)]
    public int? ElbowPain { get; set; }

    [Range(1, 10)]
    public int? ShoulderTightness { get; set; }

    // Comma-separated: Forehand, Backhand, Footwork, Serve, Fitness
    public string BreakdownAreas { get; set; } = string.Empty;

    // Comma-separated: Late to ball, Poor timing, Bad decision, Low energy
    public string BreakdownReasons { get; set; } = string.Empty;

    // Focus tracking
    public string? FocusArea { get; set; }
    public bool? FocusAchieved { get; set; }

    public string? Notes { get; set; }

    // Session rating (1-5 emoji scale: 😫😕😐🙂🔥)
    [Range(1, 5)]
    public int? SessionRating { get; set; }

    // Session type: Practice, Match, Drill, Hitting
    public string? SessionType { get; set; }

    // Match context (optional, for richer AI coaching)
    public string? OpponentLevel { get; set; }   // Below me, Similar, Above me
    public string? PlayStyle { get; set; }        // Aggressive, Defensive, All-Court, Counter-Puncher
    public string? MentalState { get; set; }      // Confident, Neutral, Frustrated, Choked
    public string? MatchResult { get; set; }      // Won, Lost, N/A

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<GoalCheckIn> GoalCheckIns { get; set; } = [];

    // Helper properties (not mapped)
    public List<string> BreakdownAreaList =>
        string.IsNullOrEmpty(BreakdownAreas) ? [] : BreakdownAreas.Split(',').ToList();

    public List<string> BreakdownReasonList =>
        string.IsNullOrEmpty(BreakdownReasons) ? [] : BreakdownReasons.Split(',').ToList();
}
