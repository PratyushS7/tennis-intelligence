using System.Text;

namespace TennisIntelligence.Services;

public class RuleBasedCoachProvider : ICoachProvider
{
    public string ProviderName => "Rule-Based Coach";
    public bool IsAvailable => true;

    public Task<string> GetCoachingAsync(string userMessage, SessionContext context, CancellationToken ct = default)
        => GetCoachingAsync(userMessage, context, [], ct);

    public Task<string> GetCoachingAsync(string userMessage, SessionContext context, List<ChatMessage> conversationHistory, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var msg = userMessage.ToLowerInvariant();

        if (context.TotalSessions == 0)
        {
            sb.AppendLine("### 👋 Welcome to Tennis Intelligence!");
            sb.AppendLine("I don't have any session data yet. Log a few sessions and come back — I'll have personalized tips ready for you!");
            return Task.FromResult(sb.ToString());
        }

        if (msg.Contains("mental") || msg.Contains("confident") || msg.Contains("frustrated") || msg.Contains("choke") || msg.Contains("pressure"))
            BuildMentalAnalysis(sb, context);
        else if (msg.Contains("style") || msg.Contains("tactic") || msg.Contains("aggressive") || msg.Contains("defensive"))
            BuildTacticalAnalysis(sb, context);
        else if (msg.Contains("opponent") || msg.Contains("match") || msg.Contains("win") || msg.Contains("lose") || msg.Contains("record"))
            BuildOpponentAnalysis(sb, context);
        else if (msg.Contains("focus") || msg.Contains("next session") || msg.Contains("work on"))
            BuildFocusSuggestion(sb, context);
        else if (msg.Contains("drill") || msg.Contains("exercise") || msg.Contains("practice"))
            BuildDrillSuggestions(sb, context);
        else if (msg.Contains("pain") || msg.Contains("injury") || msg.Contains("elbow") || msg.Contains("shoulder"))
            BuildPainAnalysis(sb, context);
        else if (msg.Contains("week") || msg.Contains("summary") || msg.Contains("review") || msg.Contains("analyze"))
            BuildWeeklySummary(sb, context);
        else
            BuildGeneralCoaching(sb, context);

        return Task.FromResult(sb.ToString());
    }

    private static void BuildFocusSuggestion(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 🎯 Focus Suggestion for Next Session");
        sb.AppendLine();

        var topBreakdown = ctx.BreakdownAreaCounts.OrderByDescending(x => x.Value).FirstOrDefault();
        if (topBreakdown.Key != null)
        {
            sb.AppendLine($"Your **{topBreakdown.Key}** has been breaking down most frequently ({topBreakdown.Value} times). I'd recommend making that your primary focus.");
            sb.AppendLine();
        }

        var topReason = ctx.BreakdownReasonCounts.OrderByDescending(x => x.Value).FirstOrDefault();
        if (topReason.Key != null)
        {
            sb.AppendLine($"The most common reason is **{topReason.Key}** — keep that in mind during play.");
            sb.AppendLine();
        }

        if (ctx.FocusTotalCount > 0)
        {
            var pct = (int)(100.0 * ctx.FocusAchievedCount / ctx.FocusTotalCount);
            if (pct < 50)
                sb.AppendLine("💡 Your focus achievement rate is below 50%. Try setting a smaller, more specific focus goal next time.");
            else
                sb.AppendLine($"✅ Great job — you're achieving your focus goals {pct}% of the time!");
        }
    }

    private static void BuildDrillSuggestions(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 🏋️ Drill Suggestions Based on Your Data");
        sb.AppendLine();

        var areas = ctx.BreakdownAreaCounts.OrderByDescending(x => x.Value).Take(3);
        foreach (var (area, count) in areas)
        {
            sb.AppendLine($"**{area}** (broke down {count} time(s)):");
            switch (area.ToLower())
            {
                case "forehand":
                    sb.AppendLine("- Shadow swing drill: 50 forehand swings focusing on early racket preparation");
                    sb.AppendLine("- Cross-court rally drill: Hit 20 balls cross-court, focus on follow-through");
                    break;
                case "backhand":
                    sb.AppendLine("- Wall rally drill: Hit 50 backhands against a wall, focus on contact point");
                    sb.AppendLine("- Two-handed backhand: Practice unit turn and shoulder rotation");
                    break;
                case "footwork":
                    sb.AppendLine("- Split step drill: Practice split-stepping before every shot in rallies");
                    sb.AppendLine("- Ladder drills: 10 minutes of agility ladder work before playing");
                    break;
                case "serve":
                    sb.AppendLine("- Toss consistency drill: 20 tosses without hitting, catch at peak height");
                    sb.AppendLine("- Serve targets: Place cones and aim for 10 serves to each target");
                    break;
                case "fitness":
                    sb.AppendLine("- Interval training: 30 seconds sprint, 30 seconds rest, 10 rounds");
                    sb.AppendLine("- Court suicides: Side-to-side court sprints, 5 sets");
                    break;
            }
            sb.AppendLine();
        }
    }

    private static void BuildPainAnalysis(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 💪 Pain & Injury Analysis");
        sb.AppendLine();
        sb.AppendLine($"- Average elbow pain: **{ctx.AvgElbowPain:F1}/10**");
        sb.AppendLine($"- Average shoulder tightness: **{ctx.AvgShoulderTightness:F1}/10**");
        sb.AppendLine();

        if (ctx.AvgElbowPain >= 5)
        {
            sb.AppendLine("⚠️ **Your elbow pain is elevated.** Consider:");
            sb.AppendLine("- Using a vibration dampener on your racket");
            sb.AppendLine("- Doing forearm stretches before and after play");
            sb.AppendLine("- Reducing topspin intensity temporarily");
            sb.AppendLine("- Checking your grip size (too small can worsen elbow pain)");
            sb.AppendLine();
        }

        if (ctx.AvgShoulderTightness >= 5)
        {
            sb.AppendLine("⚠️ **Your shoulder tightness is elevated.** Consider:");
            sb.AppendLine("- Shoulder warm-up with resistance bands before play");
            sb.AppendLine("- Reducing serve power until tightness decreases");
            sb.AppendLine("- Rotator cuff strengthening exercises (3x/week)");
            sb.AppendLine();
        }

        if (ctx.AvgElbowPain < 3 && ctx.AvgShoulderTightness < 3)
            sb.AppendLine("✅ Your pain levels look good! Keep up the warm-up routine.");

        if (ctx.RecentSessions.Count >= 2)
        {
            var latest = ctx.RecentSessions[0];
            var previous = ctx.RecentSessions[1];
            if (latest.ElbowPain > previous.ElbowPain + 2)
                sb.AppendLine($"\n📈 **Warning**: Elbow pain jumped from {previous.ElbowPain} to {latest.ElbowPain} in your last session. Consider a rest day.");
            if (latest.ShoulderTightness > previous.ShoulderTightness + 2)
                sb.AppendLine($"\n📈 **Warning**: Shoulder tightness jumped from {previous.ShoulderTightness} to {latest.ShoulderTightness}. Take it easy on serves.");
        }
    }

    private static void BuildWeeklySummary(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 📊 Session Summary");
        sb.AppendLine();
        sb.AppendLine($"- **{ctx.TotalSessions}** sessions logged");
        sb.AppendLine($"- Average energy: **{ctx.AvgEnergy:F1}/10**");
        sb.AppendLine($"- Average elbow pain: **{ctx.AvgElbowPain:F1}/10**");
        sb.AppendLine($"- Average shoulder tightness: **{ctx.AvgShoulderTightness:F1}/10**");
        sb.AppendLine();

        if (ctx.BreakdownAreaCounts.Count > 0)
        {
            sb.AppendLine("**Top breakdown areas:**");
            foreach (var (area, count) in ctx.BreakdownAreaCounts.OrderByDescending(x => x.Value).Take(3))
                sb.AppendLine($"- {area}: {count}x");
            sb.AppendLine();
        }

        if (ctx.AvgEnergy < 5)
            sb.AppendLine("💡 Your energy levels are on the lower side. Consider adjusting session timing or improving sleep/nutrition.");
        else
            sb.AppendLine("⚡ Energy levels look healthy!");
    }

    private static void BuildMentalAnalysis(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 🧠 Mental Game Analysis");
        sb.AppendLine();

        if (ctx.MentalStateCounts.Count == 0)
        {
            sb.AppendLine("No mental state data logged yet. Start tracking how you feel during sessions to get insights!");
            return;
        }

        var total = ctx.MentalStateCounts.Values.Sum();
        sb.AppendLine("**Mental state distribution:**");
        foreach (var (state, count) in ctx.MentalStateCounts.OrderByDescending(x => x.Value))
        {
            var pct = (int)(100.0 * count / total);
            sb.AppendLine($"- {state}: {count} session(s) ({pct}%)");
        }
        sb.AppendLine();

        if (ctx.MentalStateCounts.TryGetValue("Frustrated", out var frustratedCount) && frustratedCount > 0)
        {
            var frustratedPct = (int)(100.0 * frustratedCount / total);
            sb.AppendLine($"⚠️ You report feeling **frustrated in {frustratedPct}%** of sessions.");

            // Cross-reference: what breaks down when frustrated?
            var frustratedSessions = ctx.RecentSessions
                .Where(s => s.MentalState == "Frustrated" && !string.IsNullOrEmpty(s.BreakdownAreas))
                .ToList();
            if (frustratedSessions.Count > 0)
            {
                var commonBreakdown = frustratedSessions
                    .SelectMany(s => s.BreakdownAreas.Split(','))
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .GroupBy(a => a.Trim())
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                if (commonBreakdown != null)
                    sb.AppendLine($"  When frustrated, your **{commonBreakdown.Key}** tends to break down most.");
            }

            sb.AppendLine();
            sb.AppendLine("💡 **Tips to manage frustration:**");
            sb.AppendLine("- Try a **4-7-8 breathing routine** between points (inhale 4s, hold 7s, exhale 8s)");
            sb.AppendLine("- Focus on the process, not the score — pick one technical cue per game");
            sb.AppendLine("- Use a physical reset routine (bounce the ball, adjust strings) to break negative cycles");
            sb.AppendLine();
        }

        if (ctx.MentalStateCounts.TryGetValue("Choked", out var chokedCount) && chokedCount > 0)
        {
            sb.AppendLine($"🎯 You've reported choking in **{chokedCount}** session(s).");
            sb.AppendLine("- Practice pressure drills: play tie-break sets starting at 4-4");
            sb.AppendLine("- Develop a pre-point routine to stay present under pressure");
            sb.AppendLine();
        }

        if (ctx.MentalStateCounts.TryGetValue("Confident", out var confidentCount) && confidentCount > 0)
        {
            var confidentPct = (int)(100.0 * confidentCount / total);
            sb.AppendLine($"✅ You feel **confident in {confidentPct}%** of sessions — keep building on what works!");
        }
    }

    private static void BuildTacticalAnalysis(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 🎾 Tactical & Style Analysis");
        sb.AppendLine();

        if (ctx.PlayStyleCounts.Count == 0)
        {
            sb.AppendLine("No play style data logged yet. Start tracking your play style to get tactical insights!");
            return;
        }

        var total = ctx.PlayStyleCounts.Values.Sum();
        sb.AppendLine("**Play style distribution:**");
        foreach (var (style, count) in ctx.PlayStyleCounts.OrderByDescending(x => x.Value))
        {
            var pct = (int)(100.0 * count / total);
            sb.AppendLine($"- {style}: {count} session(s) ({pct}%)");
        }
        sb.AppendLine();

        // Cross-reference play style with win rate
        if (ctx.WinRateByOpponentLevel.Count > 0 || ctx.MatchesWon + ctx.MatchesLost > 0)
        {
            var styleWins = ctx.RecentSessions
                .Where(s => !string.IsNullOrEmpty(s.PlayStyle) && s.MatchResult is "Won" or "Lost")
                .GroupBy(s => s.PlayStyle!)
                .Select(g => new { Style = g.Key, Wins = g.Count(s => s.MatchResult == "Won"), Total = g.Count() })
                .OrderByDescending(x => x.Total)
                .ToList();

            if (styleWins.Count > 0)
            {
                sb.AppendLine("**Win rate by play style (recent sessions):**");
                foreach (var sw in styleWins)
                {
                    var pct = (int)(100.0 * sw.Wins / sw.Total);
                    sb.AppendLine($"- {sw.Style}: {sw.Wins}/{sw.Total} ({pct}%)");
                }

                var bestStyle = styleWins.Where(s => s.Total >= 2).OrderByDescending(s => (double)s.Wins / s.Total).FirstOrDefault();
                if (bestStyle != null)
                    sb.AppendLine($"\n💡 You tend to win more when playing **{bestStyle.Style}**. Consider leaning into that approach.");
                sb.AppendLine();
            }
        }

        var topStyle = ctx.PlayStyleCounts.OrderByDescending(x => x.Value).First();
        sb.AppendLine($"🎯 Your most common style is **{topStyle.Key}**.");
        switch (topStyle.Key)
        {
            case "Aggressive":
                sb.AppendLine("- Make sure you're picking your spots — don't force winners on every ball");
                sb.AppendLine("- Work on approach shots to set up easier put-aways");
                break;
            case "Defensive":
                sb.AppendLine("- Try adding one offensive shot per rally to keep opponents guessing");
                sb.AppendLine("- Work on turning defense into offense with deep, heavy balls");
                break;
            case "All-Court":
                sb.AppendLine("- Great versatility! Focus on reading the ball early to choose the right tactic");
                sb.AppendLine("- Practice transitioning between baseline and net play");
                break;
            case "Counter-Puncher":
                sb.AppendLine("- Your patience is an asset — work on recognizing short balls to attack");
                sb.AppendLine("- Add drop shots to your arsenal to pull aggressive opponents forward");
                break;
        }
    }

    private static void BuildOpponentAnalysis(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 📊 Match & Opponent Analysis");
        sb.AppendLine();

        if (ctx.MatchesWon + ctx.MatchesLost == 0)
        {
            sb.AppendLine("No match results logged yet. Start tracking your match outcomes to see patterns!");
            return;
        }

        sb.AppendLine($"**Overall match record:** {ctx.MatchesWon}W - {ctx.MatchesLost}L");
        var totalMatches = ctx.MatchesWon + ctx.MatchesLost;
        if (totalMatches > 0)
        {
            var winPct = (int)(100.0 * ctx.MatchesWon / totalMatches);
            sb.AppendLine($"**Win rate:** {winPct}%");
        }
        sb.AppendLine();

        if (ctx.WinRateByOpponentLevel.Count > 0)
        {
            sb.AppendLine("**Win rate by opponent level:**");
            foreach (var level in new[] { "Below me", "Similar", "Above me" })
            {
                if (ctx.WinRateByOpponentLevel.TryGetValue(level, out var record))
                {
                    var t = record.Wins + record.Losses;
                    var pct = t > 0 ? (int)(100.0 * record.Wins / t) : 0;
                    sb.AppendLine($"- {level}: {record.Wins}/{t} ({pct}%)");
                }
            }
            sb.AppendLine();

            // Identify patterns against stronger opponents
            if (ctx.WinRateByOpponentLevel.TryGetValue("Above me", out var aboveMe) && aboveMe.Wins + aboveMe.Losses >= 2)
            {
                var aboveTotal = aboveMe.Wins + aboveMe.Losses;
                var abovePct = (int)(100.0 * aboveMe.Wins / aboveTotal);
                if (abovePct < 40)
                {
                    sb.AppendLine($"💡 You're winning only **{abovePct}%** against stronger opponents.");

                    // Check if they tend to play defensive against strong opponents
                    var strongOpponentStyles = ctx.RecentSessions
                        .Where(s => s.OpponentLevel == "Above me" && !string.IsNullOrEmpty(s.PlayStyle))
                        .GroupBy(s => s.PlayStyle!)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();
                    if (strongOpponentStyles != null)
                        sb.AppendLine($"  Against stronger opponents, you tend to play **{strongOpponentStyles.Key}**.");

                    sb.AppendLine("  Try mixing up your tactics — surprise them with a different approach.");
                    sb.AppendLine();
                }
            }
        }

        if (ctx.OpponentLevelCounts.Count > 0)
        {
            sb.AppendLine("**Opponent level distribution:**");
            foreach (var (level, count) in ctx.OpponentLevelCounts.OrderByDescending(x => x.Value))
                sb.AppendLine($"- {level}: {count} session(s)");
        }
    }

    private static void BuildGeneralCoaching(StringBuilder sb, SessionContext ctx)
    {
        sb.AppendLine("### 🤖 Your Tennis Coach Report");
        sb.AppendLine();
        BuildWeeklySummary(sb, ctx);

        // Brief game profile
        if (ctx.MatchesWon + ctx.MatchesLost > 0 || ctx.PlayStyleCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**🎾 Game Profile:**");
            if (ctx.PlayStyleCounts.Count > 0)
            {
                var topStyle = ctx.PlayStyleCounts.OrderByDescending(x => x.Value).First();
                sb.AppendLine($"- Preferred style: {topStyle.Key}");
            }
            if (ctx.MatchesWon + ctx.MatchesLost > 0)
                sb.AppendLine($"- Match record: {ctx.MatchesWon}W - {ctx.MatchesLost}L");
            if (ctx.MentalStateCounts.Count > 0)
            {
                var topMental = ctx.MentalStateCounts.OrderByDescending(x => x.Value).First();
                sb.AppendLine($"- Most common mental state: {topMental.Key}");
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
        BuildFocusSuggestion(sb, ctx);
        sb.AppendLine("---");
        sb.AppendLine();
        BuildPainAnalysis(sb, ctx);

        // Usage-based nudges
        BuildUsageNudges(sb, ctx);
    }

    private static void BuildUsageNudges(StringBuilder sb, SessionContext ctx)
    {
        if (ctx.Usage == null)
            return;

        var nudges = new List<string>();

        if (ctx.Usage.DaysSinceLastSessionLog > 7)
            nudges.Add($"⏰ It's been **{ctx.Usage.DaysSinceLastSessionLog} days** since you logged a session. Consistency is key — even a short hit is worth tracking!");

        if (ctx.Usage.InsightsPageViewCount == 0)
            nudges.Add("📊 You haven't checked your **Insights** page yet! Head there to see patterns in your game.");
        else if (ctx.Usage.InsightsPageViewCount < 3)
            nudges.Add("📊 Check your **Insights** page more often — your patterns become clearer with regular review.");

        if (ctx.Usage.FieldCompletionRates.Count > 0)
        {
            if (ctx.Usage.FieldCompletionRates.TryGetValue("FocusArea", out var focusRate) && focusRate < 50)
                nudges.Add($"🎯 You set a focus area only **{focusRate}%** of the time. Players who set a focus improve faster — try it next session!");

            if (ctx.Usage.FieldCompletionRates.TryGetValue("Notes", out var notesRate) && notesRate < 30)
                nudges.Add($"📝 You add notes only **{notesRate}%** of the time. Even a sentence helps me give better advice.");

            if (ctx.Usage.FieldCompletionRates.TryGetValue("SessionRating", out var ratingRate) && ratingRate < 40)
                nudges.Add($"⭐ You rate sessions only **{ratingRate}%** of the time. Ratings help identify what makes a great session.");
        }

        if (ctx.Usage.CoachQuestionsAsked == 0)
            nudges.Add("💬 You haven't asked me anything yet! Try asking about your weakest area or request drill suggestions.");

        if (ctx.Usage.CurrentWeekVisitCount >= 5)
            nudges.Add("🔥 You've been active in the app this week — great engagement!");

        if (nudges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("### 💡 Tracking Tips");
            foreach (var nudge in nudges.Take(3))
                sb.AppendLine($"- {nudge}");
        }
    }
}
