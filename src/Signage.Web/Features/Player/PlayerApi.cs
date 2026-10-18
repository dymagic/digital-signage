using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Signage.Web.Features.Pairing;
using Signage.Web.Features.Hosting;
using Signage.Web.Features.Users;

namespace Signage.Web.Features.Player;

/// <summary>The player's HTTP API (ARCHITECTURE §4). Pairing and the token exchange are anonymous and rate-limited.</summary>
public static class PlayerApi
{
    public const string ScreenPolicy = "Screen";
    public const string DeviceSecretHeader = "X-Device-Secret";
    public const string PairingStartLimit = "pairing-start";
    public const string PairingPollLimit = "pairing-poll";
    public const string SessionLimit = "screen-session";

    public static IServiceCollection AddSignagePlayerApi(this IServiceCollection services)
    {
        services.AddScoped<PairingService>();
        services.AddScoped<ScreenAccessService>();
        services.AddScoped<ManifestService>();
        services.AddScoped<HeartbeatService>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ScreenAuthenticationHandler>(ScreenAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(ScreenPolicy, p => p
            .AddAuthenticationSchemes(ScreenAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());

        // Per client IP. Per replica as well: with N replicas an attacker gets N times the budget, which is still far
        // from enough to guess a code (25^6) or a token (2^256). See ADR-0007.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(PairingStartLimit, ctx => FixedWindowPerIp(ctx, permits: 10));
            o.AddPolicy(PairingPollLimit, ctx => FixedWindowPerIp(ctx, permits: 60));
            o.AddPolicy(SessionLimit, ctx => FixedWindowPerIp(ctx,
                permits: Math.Max(1, ctx.RequestServices.GetRequiredService<IOptions<SignageOptions>>().Value.ScreenAddressExchangesPerMinute)));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapSignagePlayerApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/player").DisableAntiforgery();

        api.MapPost("/pairing", async (PairingService pairing, HttpContext http, CancellationToken ct) =>
        {
            NoStore(http);
            var start = await pairing.StartAsync(ct).ConfigureAwait(false);
            // expiresInSeconds lets the device time the refresh without trusting its own clock.
            return Results.Ok(new { code = PairingCode.Format(start.Code), pairingId = start.PairingId, deviceSecret = start.DeviceSecret, expiresAt = start.ExpiresAt, expiresInSeconds = (int)start.Lifetime.TotalSeconds });
        }).RequireRateLimiting(PairingStartLimit);

        api.MapGet("/pairing/{id:guid}", async (Guid id, PairingService pairing, HttpContext http, TimeProvider time, CancellationToken ct) =>
        {
            NoStore(http);
            var poll = await pairing.PollAsync(id, http.Request.Headers[DeviceSecretHeader].ToString(), ct).ConfigureAwait(false);
            switch (poll.Status)
            {
                case PairingPollStatus.Pending:
                    return Results.Ok(new { status = "pending", expiresAt = poll.ExpiresAt });
                case PairingPollStatus.Paired:
                    ScreenAuthenticationHandler.SetCookie(http, poll.Token!, time);
                    return Results.Ok(new { status = "paired", screen = new { name = poll.ScreenName } });
                case PairingPollStatus.Gone:
                    return Results.Json(new { status = "expired" }, statusCode: StatusCodes.Status410Gone);
                default:
                    return Results.NotFound();
            }
        }).RequireRateLimiting(PairingPollLimit);

        // R-41: a pre-configured screen address (/screen?token=…) is exchanged for the cookie.
        api.MapPost("/session", async (SessionRequest request, ScreenAccessService access, HttpContext http, TimeProvider time, CancellationToken ct) =>
        {
            NoStore(http);
            if (await access.FindScreenAsync(request.Token, ct).ConfigureAwait(false) is null)
            {
                return Results.Unauthorized();
            }

            ScreenAuthenticationHandler.SetCookie(http, request.Token!, time);
            return Results.NoContent();
        }).RequireRateLimiting(SessionLimit);

        // The screen's manifest (R-13, R-24, R-25). Supports If-None-Match so the 60 s poll is cheap.
        api.MapGet("/manifest", async (HttpContext http, ManifestService manifests, CancellationToken ct) =>
        {
            var manifest = await manifests.BuildAsync(http.User.ScreenId(), http.ObservedIp(), ct).ConfigureAwait(false);
            return manifest is null ? Results.Unauthorized() : ManifestResult(http, manifest);
        }).RequireAuthorization(ScreenPolicy);

        // R-25: pushes "fetch your manifest" (and later identify) to the screen.
        api.MapGet("/events", PlayerEvents.StreamAsync).RequireAuthorization(ScreenPolicy);

        // R-33: last seen, observed IP, user agent and player version; problems are logged.
        api.MapPost("/heartbeat", async (HeartbeatRequest request, HttpContext http, HeartbeatService heartbeats, CancellationToken ct) =>
        {
            NoStore(http);
            await heartbeats.RecordAsync(http.User.ScreenId(), request, http.ObservedIp(),
                http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
            return Results.NoContent();
        }).RequireAuthorization(ScreenPolicy);

        // R-13: an editor or admin previews a screen in their own browser (/screen?preview={id}). Uses the admin cookie;
        // the preview never records anything about a device.
        api.MapGet("/preview/{screenId:guid}/manifest", async (Guid screenId, HttpContext http, ManifestService manifests, CancellationToken ct) =>
        {
            if (http.User.MustChangePassword())
            {
                return Results.Forbid();
            }

            var manifest = await manifests.BuildAsync(screenId, observedIp: null, ct).ConfigureAwait(false);
            return manifest is null ? Results.NotFound() : ManifestResult(http, manifest);
        }).RequireAuthorization(Policies.Editor);

        return endpoints;
    }

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";

    private static IResult ManifestResult(HttpContext http, Manifest manifest)
    {
        var (body, etag) = ManifestService.Serialize(manifest);
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers.ETag = etag;
        return Matches(http.Request.Headers.IfNoneMatch, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(body, "application/json; charset=utf-8");
    }

    // Weak comparison, as If-None-Match requires (RFC 9110 §13.1.2): a compressing proxy turns the tag into W/"…".
    private static bool Matches(StringValues ifNoneMatch, string etag) =>
        EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags)
        && tags.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false));

    private static RateLimitPartition<string> FixedWindowPerIp(HttpContext ctx, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    public sealed record SessionRequest(string? Token);
}
