using Npgsql;

namespace Signage.Web.Features.Events;

/// <summary>
/// Sends player events to every replica through <c>NOTIFY signage_events</c> (R-25, R-63). Each replica's
/// <see cref="ScreenEventListener"/>, this one included, hands them to its connected players. Callers notify after their change is
/// saved, so a failure here is logged, never thrown: the change stands, and players get it with their next poll (60 s).
/// </summary>
public sealed partial class PostgresScreenNotifier(NpgsqlDataSource dataSource, ILogger<PostgresScreenNotifier> logger) : IScreenNotifier
{
    public const string Channel = "signage_events";

    // NOTIFY payloads are limited to 8000 bytes; a GUID takes about 40 in JSON.
    private const int ScreensPerNotification = 150;

    public async Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(screenIds);
        foreach (var chunk in screenIds.Distinct().Chunk(ScreensPerNotification))
        {
            await SendAsync(new ScreenEvent(ScreenEvent.ManifestChanged, chunk), ct).ConfigureAwait(false);
        }
    }

    public Task AllScreensChangedAsync(CancellationToken ct) => SendAsync(ScreenEvent.AllManifestsChanged, ct);

    public async Task<bool> IdentifyAsync(IReadOnlyCollection<Guid>? screenIds, CancellationToken ct)
    {
        if (screenIds is null)
        {
            return await SendAsync(new ScreenEvent(ScreenEvent.IdentifyAll, null, ScreenEvent.IdentifySeconds), ct).ConfigureAwait(false);
        }
        var sent = true;
        foreach (var chunk in screenIds.Distinct().Chunk(ScreensPerNotification))
        {
            sent &= await SendAsync(new ScreenEvent(ScreenEvent.Identify, chunk, ScreenEvent.IdentifySeconds), ct).ConfigureAwait(false);
        }
        return sent;
    }

    /// <summary>Sends the event; false (and a warning in the log) if Postgres couldn't take it.</summary>
    public async Task<bool> SendAsync(ScreenEvent screenEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(screenEvent);
        try
        {
            await using var command = dataSource.CreateCommand("SELECT pg_notify(@channel, @payload)");
            command.Parameters.AddWithValue("channel", Channel);
            command.Parameters.AddWithValue("payload", screenEvent.ToJson());
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            if (screenEvent.Kind == ScreenEvent.ManifestChanged)
            {
                LogNotifyFailed(logger, ex, screenEvent.Kind);
            }
            else
            {
                LogEventLost(logger, ex, screenEvent.Kind);
            }
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send a {Kind} event to players; it is lost (the admin is told to try again)")]
    private static partial void LogEventLost(ILogger logger, Exception ex, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send a {Kind} event to players; they pick the change up with their next poll")]
    private static partial void LogNotifyFailed(ILogger logger, Exception ex, string kind);
}
