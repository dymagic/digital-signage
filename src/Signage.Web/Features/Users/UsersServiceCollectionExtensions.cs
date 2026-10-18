using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Signage.Web.Domain;

namespace Signage.Web.Features.Users;

public static class UsersServiceCollectionExtensions
{
    public const string CookieName = "signage.auth";

    public static IServiceCollection AddSignageAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<UserAccounts>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // Secure whenever the request is HTTPS (directly or via trusted forwarded headers). Plain-HTTP
                // LAN installs still work; see ADR-0004 and the README security note.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/admin/login";
                options.LogoutPath = "/admin/logout";
                options.AccessDeniedPath = "/admin/forbidden";
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = UserPrincipal.ValidateAsync;

                // APIs (e.g. the admin preview manifest) answer 401/403 instead of redirecting to the sign-in page.
                options.Events.OnRedirectToLogin = ctx => ApiAware(ctx, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx, StatusCodes.Status403Forbidden);

                // Background refreshes (the screens status list, R-33) don't count as activity: an open tab must not keep a
                // session alive for ever (ADR-0010).
                options.Events.OnCheckSlidingExpiration = ctx =>
                {
                    if (ctx.Request.Headers.ContainsKey(BackgroundRefreshHeader))
                    {
                        ctx.ShouldRenew = false;
                    }
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(Policies.Editor, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Editor)));

        return services;
    }

    /// <summary>Sent by background htmx refreshes; such requests never extend the session.</summary>
    public const string BackgroundRefreshHeader = "X-Background-Refresh";

    private static Task ApiAware(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = status;
        }
        else if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            // htmx would follow a redirect and swap the sign-in page into a fragment (or nothing): send the browser there.
            ctx.Response.StatusCode = status;
            ctx.Response.Headers["HX-Redirect"] = ctx.RedirectUri;
        }
        else
        {
            ctx.Response.Redirect(ctx.RedirectUri);
        }
        return Task.CompletedTask;
    }
}
