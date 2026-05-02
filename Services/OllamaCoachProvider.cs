using System.Text;
using System.Text.Json;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

public class OllamaCoachProvider : ICoachProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;
    private bool? _cachedAvailability;
    private DateTime _lastCheck = DateTime.MinValue;

    public string ProviderName => $"Ollama ({_model})";

    public bool IsAvailable
    {
        get
        {
            // Cache availability for 60 seconds
            if (_cachedAvailability.HasValue && (DateTime.UtcNow - _lastCheck).TotalSeconds < 60)
                return _cachedAvailability.Value;

            // Don't block — just try a synchronous TCP connect
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var uri = new Uri(_baseUrl);
                tcp.Connect(uri.Host, uri.Port);
                _cachedAvailability = true;
            }
            catch { _cachedAvailability = false; }

            _lastCheck = DateTime.UtcNow;
            return _cachedAvailability.Value;
        }
    }

    public OllamaCoachProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["Coach:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = config["Coach:Ollama:Model"] ?? "llama3.2";
        _http.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<string> GetCoachingAsync(string userMessage, SessionContext context, CancellationToken ct = default)
        => await GetCoachingAsync(userMessage, context, [], ct);

    public async Task<string> GetCoachingAsync(string userMessage, SessionContext context, List<ChatMessage> conversationHistory, CancellationToken ct = default)
    {
        var systemPrompt = BuildSystemPrompt(context);

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        // Add conversation history for multi-turn context
        foreach (var msg in conversationHistory)
            messages.Add(new { role = msg.Role, content = msg.Content });

        // Add the current user message
        messages.Add(new { role = "user", content = userMessage });

        var request = new
        {
            model = _model,
            messages,
            stream = false,
            options = new { temperature = 0.7, num_predict = 512 }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/api/chat", content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "No response from AI.";
    }

    private static readonly string[] RatingEmojis = ["", "😫", "😕", "😐", "🙂", "🔥"];

    private static string BuildSystemPrompt(SessionContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an experienced tennis coach analyzing a casual player's training data.");
        sb.AppendLine("You are building a long-term understanding of this player's game style, tactical tendencies, and mental patterns.");
        sb.AppendLine("Use their match context data to provide increasingly personalized advice.");
        sb.AppendLine("Your job is to provide personalized, actionable advice based on their session history.");
        sb.AppendLine("Be encouraging but honest. Keep responses concise and practical.");
        sb.AppendLine("Use bullet points and structure your advice clearly.");
        sb.AppendLine("If suggesting drills, explain them briefly so a casual player can follow along.");
        sb.AppendLine();
        sb.AppendLine("## Player Data Summary");
        sb.AppendLine($"- Total sessions logged: {ctx.TotalSessions}");
        sb.AppendLine($"- Average energy level: {ctx.AvgEnergy:F1}/10");
        sb.AppendLine($"- Average elbow pain: {ctx.AvgElbowPain:F1}/10");
        sb.AppendLine($"- Average shoulder tightness: {ctx.AvgShoulderTightness:F1}/10");

        if (ctx.FocusTotalCount > 0)
        {
            var pct = (int)(100.0 * ctx.FocusAchievedCount / ctx.FocusTotalCount);
            sb.AppendLine($"- Focus achievement rate: {pct}% ({ctx.FocusAchievedCount}/{ctx.FocusTotalCount})");
        }

        // Game profile section
        if (ctx.PlayStyleCounts.Count > 0 || ctx.MatchesWon + ctx.MatchesLost > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Player Game Profile");

            if (ctx.PlayStyleCounts.Count > 0)
            {
                var topStyle = ctx.PlayStyleCounts.OrderByDescending(x => x.Value).First();
                sb.AppendLine($"- Preferred play style: {topStyle.Key} ({topStyle.Value} sessions)");
                sb.Append("- Play style distribution: ");
                sb.AppendLine(string.Join(", ", ctx.PlayStyleCounts.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}: {x.Value}")));
            }

            if (ctx.MentalStateCounts.Count > 0)
            {
                sb.Append("- Mental state distribution: ");
                sb.AppendLine(string.Join(", ", ctx.MentalStateCounts.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key}: {x.Value}")));
            }

            if (ctx.MatchesWon + ctx.MatchesLost > 0)
                sb.AppendLine($"- Match record: {ctx.MatchesWon} wins, {ctx.MatchesLost} losses");

            if (ctx.WinRateByOpponentLevel.Count > 0)
            {
                sb.AppendLine("- Win rate by opponent level:");
                foreach (var level in new[] { "Below me", "Similar", "Above me" })
                {
                    if (ctx.WinRateByOpponentLevel.TryGetValue(level, out var record))
                    {
                        var total = record.Wins + record.Losses;
                        var pct = total > 0 ? (int)(100.0 * record.Wins / total) : 0;
                        sb.AppendLine($"  - {level}: {record.Wins}/{total} ({pct}%)");
                    }
                }
            }
        }

        if (ctx.BreakdownAreaCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Most Common Breakdown Areas");
            foreach (var (area, count) in ctx.BreakdownAreaCounts.OrderByDescending(x => x.Value).Take(5))
                sb.AppendLine($"- {area}: {count} time(s)");
        }

        if (ctx.BreakdownReasonCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Most Common Breakdown Reasons");
            foreach (var (reason, count) in ctx.BreakdownReasonCounts.OrderByDescending(x => x.Value).Take(5))
                sb.AppendLine($"- {reason}: {count} time(s)");
        }

        if (ctx.RecentSessions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Recent Sessions");
            foreach (var s in ctx.RecentSessions.Take(10))
            {
                var parts = new List<string>
                {
                    $"{s.DurationMinutes}min",
                    $"Energy {s.EnergyLevel}/10"
                };

                if (s.SessionRating is >= 1 and <= 5)
                    parts.Add($"Rating {RatingEmojis[s.SessionRating.Value]}");
                if (!string.IsNullOrEmpty(s.SessionType))
                    parts.Add(s.SessionType);
                if (!string.IsNullOrEmpty(s.OpponentLevel))
                    parts.Add($"vs {s.OpponentLevel} opponent");
                if (!string.IsNullOrEmpty(s.PlayStyle))
                    parts.Add(s.PlayStyle);
                if (!string.IsNullOrEmpty(s.MentalState))
                    parts.Add(s.MentalState);
                if (!string.IsNullOrEmpty(s.MatchResult) && s.MatchResult != "N/A")
                    parts.Add(s.MatchResult);

                sb.AppendLine($"- {s.Date:MMM dd}: {string.Join(", ", parts)}");

                if (!string.IsNullOrEmpty(s.BreakdownAreas))
                    sb.AppendLine($"  Breakdowns: {s.BreakdownAreas}");
                if (!string.IsNullOrEmpty(s.BreakdownReasons))
                    sb.AppendLine($"  Reasons: {s.BreakdownReasons}");
                if (!string.IsNullOrEmpty(s.FocusArea))
                    sb.AppendLine($"  Focus: {s.FocusArea} → {(s.FocusAchieved == true ? "Achieved ✅" : s.FocusAchieved == false ? "Not achieved ❌" : "N/A")}");
                if (!string.IsNullOrEmpty(s.Notes))
                    sb.AppendLine($"  Notes: {s.Notes}");
            }
        }

        // Development goals context (v2)
        if (ctx.ActiveGoals.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Active Development Goals");
            sb.AppendLine("These are the player's current focus areas. Prioritize advice that relates to these goals.");
            foreach (var g in ctx.ActiveGoals)
            {
                sb.Append($"- **{g.Name}** ({g.Category})");
                if (g.TotalCheckIns > 0)
                {
                    sb.Append($" — {g.TotalCheckIns} check-ins (😤{g.StruggledCount} 😐{g.OkayCount} ✅{g.ClickedCount})");
                    if (g.Last5Feelings.Count > 0)
                        sb.Append($" Recent: {string.Join(" ", g.Last5Feelings.Select(GoalFeelings.ToEmoji))}");
                }
                else
                {
                    sb.Append(" — new goal, no check-ins yet");
                }
                sb.AppendLine();
            }
        }

        if (ctx.RecentlyCompleted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Recently Completed Goals (last 30 days)");
            foreach (var g in ctx.RecentlyCompleted)
                sb.AppendLine($"- **{g.Name}** ({g.Category}) — completed after {g.DaysActive} days, {g.TotalCheckIns} check-ins");
        }

        // App usage context for feedback loop
        if (ctx.Usage != null)
        {
            sb.AppendLine();
            sb.AppendLine("## App Usage Patterns");
            sb.AppendLine("Use this to give the player contextual nudges about their tracking habits.");

            if (ctx.Usage.DaysSinceLastSessionLog >= 0)
                sb.AppendLine($"- Days since last session logged: {ctx.Usage.DaysSinceLastSessionLog}");
            if (ctx.Usage.SessionLoggingFrequencyDays > 0)
                sb.AppendLine($"- Average days between sessions: {ctx.Usage.SessionLoggingFrequencyDays:F1}");
            if (ctx.Usage.CoachQuestionsAsked > 0)
                sb.AppendLine($"- Coach questions asked (last 30 days): {ctx.Usage.CoachQuestionsAsked}");
            sb.AppendLine($"- Insights page views (last 30 days): {ctx.Usage.InsightsPageViewCount}");
            sb.AppendLine($"- App visits this week: {ctx.Usage.CurrentWeekVisitCount}");

            if (ctx.Usage.FieldCompletionRates.Count > 0)
            {
                sb.AppendLine("- Field completion rates:");
                foreach (var (field, rate) in ctx.Usage.FieldCompletionRates)
                    sb.AppendLine($"  - {field}: {rate}%");
            }
        }

        return sb.ToString();
    }
}
