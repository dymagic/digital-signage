using Npgsql;

namespace Signage.Web.Features.Events;

/// <summary>
/// Sends player events to every replica through <c>NOTIFY signage_events</c> (R-25, R-63). Each replica's
/// <see cref="ScreenEventListener"/>, this one included, hands them to its connected players.
/// </summary>
public sealed class PostgresScreenNotifier(NpgsqlDataSource dataSource) : IScreenNotifier
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

    public async Task SendAsync(ScreenEvent screenEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(screenEvent);
        await using var command = dataSource.CreateCommand("SELECT pg_notify(@channel, @payload)");
        command.Parameters.AddWithValue("channel", Channel);
        command.Parameters.AddWithValue("payload", screenEvent.ToJson());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
