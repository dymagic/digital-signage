using Microsoft.AspNetCore.Http.Features;
using Signage.Web.Features.Events;
using Signage.Web.Features.Pairing;

namespace Signage.Web.Features.Player;

/// <summary>
/// <c>GET /api/player/events</c>: the server-sent event stream of one screen (R-25, R-31, PLAYER_SPEC §9). `manifest-changed`
/// carries no data (the player fetches its manifest); `identify` and `identify-all` carry `{"seconds":30}`. A comment every 15 s keeps proxies from closing
/// an idle connection.
/// </summary>
internal static class PlayerEvents
{
    public static readonly TimeSpan KeepAliveEvery = TimeSpan.FromSeconds(15);

    public static async Task StreamAsync(HttpContext http, ScreenEventHub hub, TimeProvider time, IHostApplicationLifetime lifetime, CancellationToken requestAborted)
    {
        // Ends when the player goes or the app stops: an open stream must never hold up a shutdown (the player reconnects).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
        var ct = linked.Token;
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no"; // nginx: don't buffer the stream
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = hub.Subscribe(http.User.ScreenId());
        try
        {
            await response.WriteAsync("retry: 5000\n\n", ct).ConfigureAwait(false);
            await response.Body.FlushAsync(ct).ConfigureAwait(false);

            Task<bool>? next = null;
            Task? keepAlive = null;
            while (!ct.IsCancellationRequested)
            {
                next ??= subscription.Events.WaitToReadAsync(ct).AsTask();
                keepAlive ??= Task.Delay(KeepAliveEvery, time, ct);
                if (await Task.WhenAny(next, keepAlive).ConfigureAwait(false) == next)
                {
                    if (!await next.ConfigureAwait(false))
                    {
                        return;
                    }
                    next = null;
                    while (subscription.Events.TryRead(out var screenEvent))
                    {
                        await response.WriteAsync($"event: {screenEvent.Kind}\ndata: {screenEvent.DataJson()}\n\n", ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    keepAlive = null;
                    await response.WriteAsync(": keep-alive\n\n", ct).ConfigureAwait(false);
                }
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The player went away.
        }
    }
}
