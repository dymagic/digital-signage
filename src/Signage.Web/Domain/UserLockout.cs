namespace Signage.Web.Domain;

/// <summary>
/// Sign-in failure bookkeeping for one user (R-50). Kept out of <see cref="User"/> on purpose: sign-ins write
/// this row constantly, and they must never bump the user's concurrency token or race with password changes
/// (ADR-0004). No row means no recent failures.
/// </summary>
public class UserLockout
{
    public Guid UserId { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
}
