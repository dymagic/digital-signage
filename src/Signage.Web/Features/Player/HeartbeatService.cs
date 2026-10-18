using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Data.Configurations;

namespace Signage.Web.Features.Player;

/// <summary>What a player reports every 30 s (PLAYER_SPEC §9). Problems are slides it had to skip since the last one.</summary>
public sealed record HeartbeatRequest(string? PlayerVersion, Guid? CurrentSlideId, IReadOnlyList<PlayerProblem>? Problems);

/// <summary>A slide the player skipped or a single video that failed, and why (error, stalled, safety cap, …).</summary>
public sealed record PlayerProblem(Guid? SlideId, string? Kind, string? Reason);

/// <summary>
/// Records heartbeats (R-33): last seen, the IP the server observes (R-30), user agent and player version. Written with
/// a bulk update, so the screen's version and any open edit form are untouched (ADR-0004). Problems are logged.
/// </summary>
public sealed partial class HeartbeatService(SignageDbContext db, TimeProvider time, ILogger<HeartbeatService> logger)
{
    /// <summary>The player keeps at most this many (problems.js); a heartbeat with more is refused.</summary>
    public const int MaxProblems = 20;

    public async Task RecordAsync(Guid screenId, HeartbeatRequest request, string? ip, string? userAgent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        var version = Clip(request.PlayerVersion, Limits.ShortText);
        var agent = Clip(userAgent, Limits.UserAgent);
        var address = Clip(ip, Limits.Ip);
        await db.Screens.Where(s => s.Id == screenId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastSeenAt, now)
                .SetProperty(x => x.LastIp, address)
                .SetProperty(x => x.UserAgent, agent)
                .SetProperty(x => x.PlayerVersion, version), ct)
            .ConfigureAwait(false);

        foreach (var problem in (request.Problems ?? []).Take(MaxProblems))
        {
            LogProblem(logger, screenId, Clip(problem.Kind, Limits.ShortText) ?? "unknown", problem.SlideId, Clip(problem.Reason, Limits.Title) ?? "");
        }
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen {ScreenId} reported a {Kind} problem with slide {SlideId}: {Reason}")]
    private static partial void LogProblem(ILogger logger, Guid screenId, string kind, Guid? slideId, string reason);
}
