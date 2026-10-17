using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.Web.Features.Users;

/// <summary>Builds the admin cookie principal and re-checks it against the database on every request.</summary>
public static class UserPrincipal
{
    public const string SecurityStampClaim = "signage:stamp";
    public const string MustChangePasswordClaim = "signage:must_change_password";

    public static ClaimsPrincipal Create(Guid id, string email, string displayName, UserRole role, string securityStamp, bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role.ToString()),
            new(SecurityStampClaim, securityStamp),
        };
        if (mustChangePassword)
        {
            claims.Add(new Claim(MustChangePasswordClaim, "true"));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static ClaimsPrincipal Create(User user) =>
        Create(user.Id, user.Email, user.DisplayName, user.Role, user.SecurityStamp, user.MustChangePassword);

    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool MustChangePassword(this ClaimsPrincipal principal) => principal.HasClaim(MustChangePasswordClaim, "true");

    /// <summary>
    /// Cookie validation: the session ends as soon as the user is deactivated, deleted, or their security stamp
    /// changes (password reset, role change). Role, name and the must-change flag are refreshed from the database,
    /// so the cookie never carries stale permissions (R-52, R-53).
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var principal = context.Principal;
        var id = principal?.GetUserId();
        var stamp = principal?.FindFirstValue(SecurityStampClaim);
        if (id is null || stamp is null)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<SignageDbContext>();
        var current = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Email, u.DisplayName, u.Role, u.SecurityStamp, u.MustChangePassword, u.IsActive })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (current is null || !current.IsActive || !string.Equals(current.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        context.ReplacePrincipal(Create(id.Value, current.Email, current.DisplayName, current.Role, current.SecurityStamp, current.MustChangePassword));
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    }
}
