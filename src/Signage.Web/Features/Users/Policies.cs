namespace Signage.Web.Features.Users;

/// <summary>Authorization policies (PROJECT_BRIEF §3). Editor ⊂ Admin.</summary>
public static class Policies
{
    public const string Admin = "RequireAdmin";
    public const string Editor = "RequireEditor";
}
