using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Signage.Web.Features.Pairing;

namespace Signage.Web.Features.Player;

/// <summary>The player's HTTP API (ARCHITECTURE §4). Pairing and the token exchange are anonymous and rate-limited.</summary>
public static class PlayerApi
{
    public const string ScreenPolicy = "Screen";
    public const string DeviceSecretHeader = "X-Device-Secret";
    public const string PairingStartLimit = "pairing-start";
    public const string PairingPollLimit = "pairing-poll";

    public static IServiceCollection AddSignagePlayerApi(this IServiceCollection services)
    {
        services.AddScoped<PairingService>();
        services.AddScoped<ScreenAccessService>();
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
            return Results.Ok(new { code = PairingCode.Format(start.Code), pairingId = start.PairingId, deviceSecret = start.DeviceSecret, expiresAt = start.ExpiresAt });
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
        }).RequireRateLimiting(PairingStartLimit);

        // Minimal manifest: who am I. WP-08 adds the sequence, settings and the ETag.
        api.MapGet("/manifest", (HttpContext http) =>
        {
            NoStore(http);
            return Results.Ok(new { appVersion = AppInfo.Version, screen = new { id = http.User.ScreenId(), name = http.User.Identity!.Name } });
        }).RequireAuthorization(ScreenPolicy);

        return endpoints;
    }

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";

    private static RateLimitPartition<string> FixedWindowPerIp(HttpContext ctx, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    public sealed record SessionRequest(string? Token);
}
