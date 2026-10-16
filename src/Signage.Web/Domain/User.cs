namespace Signage.Web.Domain;

/// <summary>An admin-site account (R-50). Password hashing and lockout use ASP.NET Core Identity's hasher.</summary>
public class User : IHasCreatedAt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }

    /// <summary>Upper-invariant email, unique. Lookups go through this column.</summary>
    public required string NormalizedEmail { get; set; }

    public required string DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Editor;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>Changes on password reset, role change or deactivation, invalidating existing cookies.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; set; }
}
