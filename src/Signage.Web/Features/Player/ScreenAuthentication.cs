using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Signage.Web.Features.Pairing;

namespace Signage.Web.Features.Player;

/// <summary>
/// Authenticates players by their screen token, carried in an HttpOnly cookie (ADR-0007). The cookie is only sent
/// to <c>/api/player</c>, and a revoked or unknown token answers 401 and deletes the cookie, so the player
/// falls back to pairing (R-42).
/// </summary>
public sealed class ScreenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ScreenAccessService access) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScreenToken";
    public const string CookieName = "signage.screen";
    public const string CookiePath = "/api/player";
    public const string ScreenIdClaim = "signage:screen_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(CookieName, out var token))
        {
            return AuthenticateResult.NoResult();
        }

        if (await access.FindScreenAsync(token, Context.RequestAborted).ConfigureAwait(false) is not { } screen)
        {
            return AuthenticateResult.Fail("Unknown or revoked screen token.");
        }

        var identity = new ClaimsIdentity([new Claim(ScreenIdClaim, screen.Id.ToString()), new Claim(ClaimTypes.Name, screen.Name)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (Request.Cookies.ContainsKey(CookieName))
        {
            Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });
        }
        return Task.CompletedTask;
    }

    /// <summary>Sets the screen token cookie: HttpOnly, SameSite=Lax, Secure on HTTPS, long-lived.</summary>
    public static void SetCookie(HttpContext context, string token, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(time);
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = CookiePath,
            IsEssential = true,
            Expires = time.GetUtcNow().AddYears(10),
        });
    }
}

public static class ScreenPrincipal
{
    public static Guid ScreenId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ScreenAuthenticationHandler.ScreenIdClaim)!);
}
