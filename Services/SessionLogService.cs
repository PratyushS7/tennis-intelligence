using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Models;

namespace TennisIntelligence.Services;

/// <summary>A finished tennis session the watch recorded but nothing has been said about yet.</summary>
public sealed record PendingSessionLog(
    int WorkoutId,
    DateTimeOffset StartedAt,
    int DurationMinutes,
    int? AverageHeartRateBpm,
    int? MaxHeartRateBpm,
    string? Character,
    IReadOnlyList<string> BreakdownAreaOptions,
    IReadOnlyList<string> RatingEmojis);

/// <summary>What the phone collected in three taps.</summary>
public sealed record SessionLogRequest(
    int WorkoutId,
    int? Rating,
    IReadOnlyList<string>? BreakdownAreas,
    string? FocusArea);

public enum SessionLogOutcome
{
    Created,
    WorkoutNotFound,
    AlreadyLogged,
    InvalidRating
}

public sealed record SessionLogResult(SessionLogOutcome Outcome, int? SessionId = null);

/// <summary>
/// Turns a wearable workout into a logged session. The watch already knows when it happened and
/// how long it lasted, so only what it cannot measure is asked for.
/// </summary>
public sealed class SessionLogService
{
    /// <summary>Older sessions are not worth prompting about; the detail has gone by then.</summary>
    private static readonly TimeSpan PromptWindow = TimeSpan.FromDays(7);

    private readonly TennisDbContext _db;
    private readonly TrainingLoadService _trainingLoad;

    public SessionLogService(TennisDbContext db, TrainingLoadService trainingLoad)
    {
        _db = db;
        _trainingLoad = trainingLoad;
    }

    public async Task<PendingSessionLog?> GetPendingAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now - PromptWindow;

        var candidate = await _db.ExternalWorkouts
            .AsNoTracking()
            .Where(w => w.ActivityType.ToLower().Contains("tennis")
                        && w.StartedAt >= cutoff
                        && !_db.Sessions.Any(s => s.ExternalWorkoutId == w.Id))
            .OrderByDescending(w => w.StartedAt)
            .Select(w => new
            {
                w.Id,
                w.StartedAt,
                w.EndedAt,
                w.AverageHeartRateBpm,
                w.MaxHeartRateBpm
            })
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return null;

        // The character comes from the same analysis the rest of the app uses, so the prompt and
        // the training card cannot describe one session two ways.
        var report = await _trainingLoad.GetReportAsync(ct);
        var character = report.Workouts
            .FirstOrDefault(w => w.Id == candidate.Id)?.Analysis.Character;

        return new PendingSessionLog(
            candidate.Id,
            candidate.StartedAt,
            (int)Math.Round((candidate.EndedAt - candidate.StartedAt).TotalMinutes),
            candidate.AverageHeartRateBpm,
            candidate.MaxHeartRateBpm,
            character is null or SessionCharacter.Unknown ? null : character.ToString(),
            SessionVocabulary.BreakdownAreas,
            SessionVocabulary.RatingEmojis);
    }

    public async Task<SessionLogResult> LogAsync(SessionLogRequest request, DateTimeOffset now, CancellationToken ct = default)
    {
        if (request.Rating is not null and (< 1 or > 5))
            return new SessionLogResult(SessionLogOutcome.InvalidRating);

        var workout = await _db.ExternalWorkouts
            .AsNoTracking()
            .Where(w => w.Id == request.WorkoutId && w.ActivityType.ToLower().Contains("tennis"))
            .Select(w => new { w.Id, w.StartedAt, w.EndedAt })
            .FirstOrDefaultAsync(ct);

        if (workout is null) return new SessionLogResult(SessionLogOutcome.WorkoutNotFound);

        if (await _db.Sessions.AnyAsync(s => s.ExternalWorkoutId == workout.Id, ct))
            return new SessionLogResult(SessionLogOutcome.AlreadyLogged);

        // Only areas the rest of the app understands are stored, so unknown chips from an older
        // build cannot pollute the breakdown counts the coach reads.
        var areas = (request.BreakdownAreas ?? [])
            .Where(SessionVocabulary.IsKnownBreakdownArea)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var focus = SessionVocabulary.IsKnownBreakdownArea(request.FocusArea ?? string.Empty)
            ? request.FocusArea
            : null;

        var session = new Session
        {
            Date = workout.StartedAt.UtcDateTime.Date,
            DurationMinutes = Math.Clamp((int)Math.Round((workout.EndedAt - workout.StartedAt).TotalMinutes), 1, 600),
            EnergyLevel = 5,
            SessionType = "Practice",
            SessionRating = request.Rating,
            BreakdownAreas = string.Join(",", areas),
            BreakdownReasons = string.Empty,
            FocusArea = focus,
            Notes = string.Empty,
            ExternalWorkoutId = workout.Id,
            CreatedAt = now.UtcDateTime
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return new SessionLogResult(SessionLogOutcome.Created, session.Id);
    }
}
