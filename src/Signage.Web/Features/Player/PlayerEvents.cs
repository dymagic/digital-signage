using Microsoft.AspNetCore.Http.Features;
using Signage.Web.Features.Events;
using Signage.Web.Features.Pairing;

namespace Signage.Web.Features.Player;

/// <summary>
/// <c>GET /api/player/events</c>: the server-sent event stream of one screen (R-25, PLAYER_SPEC §9). Events carry no
/// data: the player fetches its manifest (or acts) when one arrives. A comment every 15 s keeps proxies from closing
/// an idle connection.
/// </summary>
internal static class PlayerEvents
{
    public static readonly TimeSpan KeepAliveEvery = TimeSpan.FromSeconds(15);

    public static async Task StreamAsync(HttpContext http, ScreenEventHub hub, TimeProvider time, CancellationToken ct)
    {
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
            while (!ct.IsCancellationRequested)
            {
                next ??= subscription.Events.WaitToReadAsync(ct).AsTask();
                var keepAlive = Task.Delay(KeepAliveEvery, time, ct);
                if (await Task.WhenAny(next, keepAlive).ConfigureAwait(false) == next)
                {
                    if (!await next.ConfigureAwait(false))
                    {
                        return;
                    }
                    next = null;
                    while (subscription.Events.TryRead(out var screenEvent))
                    {
                        await response.WriteAsync($"event: {screenEvent.Kind}\ndata: {{}}\n\n", ct).ConfigureAwait(false);
                    }
                }
                else
                {
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
