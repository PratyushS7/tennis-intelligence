namespace TennisIntelligence.Models;

/// <summary>
/// The words a session is described with. Shared so the web form, the phone prompt and any
/// analysis of past sessions cannot drift into different spellings of the same thing.
/// </summary>
public static class SessionVocabulary
{
    public static readonly string[] BreakdownAreas =
        ["Forehand", "Backhand", "Serve", "Volley", "Footwork", "Return", "Overhead", "Fitness"];

    public static readonly string[] BreakdownReasons =
        ["Late to ball", "Poor timing", "Bad decision", "Low energy", "Lack of focus", "Tight muscles", "Wrong grip", "Poor positioning"];

    public static readonly string[] SessionTypes =
        ["Practice", "Match", "Drill", "Hitting"];

    /// <summary>Emoji scale behind <see cref="Session.SessionRating"/>, worst to best.</summary>
    public static readonly string[] RatingEmojis = ["😫", "😕", "😐", "🙂", "🔥"];

    public static bool IsKnownBreakdownArea(string value) =>
        BreakdownAreas.Contains(value, StringComparer.OrdinalIgnoreCase);
}
