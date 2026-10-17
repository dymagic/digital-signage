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
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(Policies.Editor, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Editor)));

        return services;
    }
}
