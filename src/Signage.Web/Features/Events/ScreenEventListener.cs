using Npgsql;

namespace Signage.Web.Features.Events;

/// <summary>
/// Listens on <c>signage_events</c> with its own connection and publishes every event to this replica's
/// <see cref="ScreenEventHub"/> (R-63). If the connection drops it reconnects with a back-off, and afterwards tells
/// every player to fetch its manifest, because events sent in between were missed.
/// </summary>
public sealed partial class ScreenEventListener(NpgsqlDataSource dataSource, ScreenEventHub hub, TimeProvider time, ILogger<ScreenEventListener> logger)
    : BackgroundService
{
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxBackOff = TimeSpan.FromSeconds(30);
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the first LISTEN is in place (tests wait for it).</summary>
    public Task Listening => _listening.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backOff = TimeSpan.FromSeconds(1);
        var reconnect = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken).ConfigureAwait(false);
                connection.Notification += OnNotification;
                await using (var listen = new NpgsqlCommand($"LISTEN {PostgresScreenNotifier.Channel}", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
                }

                if (reconnect)
                {
                    LogReconnected(logger);
                    hub.Publish(ScreenEvent.AllManifestsChanged);
                }
                _listening.TrySetResult();
                reconnect = true;
                backOff = TimeSpan.FromSeconds(1);

                while (!stoppingToken.IsCancellationRequested)
                {
                    // A silently dropped connection would wait forever: probe it now and then.
                    if (!await connection.WaitAsync(ProbeEvery, stoppingToken).ConfigureAwait(false))
                    {
                        await using var probe = new NpgsqlCommand("SELECT 1", connection);
                        await probe.ExecuteScalarAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or IOException or TimeoutException)
            {
                LogConnectionLost(logger, ex, backOff.TotalSeconds);
                await Task.Delay(backOff, time, stoppingToken).ConfigureAwait(false);
                backOff = TimeSpan.FromTicks(Math.Min(backOff.Ticks * 2, MaxBackOff.Ticks));
                reconnect = true;
            }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (ScreenEvent.FromJson(e.Payload) is { } screenEvent)
        {
            hub.Publish(screenEvent);
        }
        else
        {
            LogBadPayload(logger, e.Payload.Length);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Event listener lost its database connection; retrying in {Seconds} s")]
    private static partial void LogConnectionLost(ILogger logger, Exception ex, double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Event listener reconnected; all players are told to fetch their manifest")]
    private static partial void LogReconnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored an event payload that isn't a screen event ({Length} chars)")]
    private static partial void LogBadPayload(ILogger logger, int length);
}
