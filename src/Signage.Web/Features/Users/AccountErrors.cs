namespace Signage.Web.Features.Users;

/// <summary>Localization keys for account errors. A unit test checks every one exists in the resource file.</summary>
public static class AccountErrors
{
    public const string InvalidEmail = "Users.Error.InvalidEmail";
    public const string EmailTaken = "Users.Error.EmailTaken";
    public const string PasswordTooShort = "Users.Error.PasswordTooShort";
    public const string PasswordTooLong = "Users.Error.PasswordTooLong";
    public const string WrongCurrentPassword = "Users.Error.WrongCurrentPassword";
    public const string SamePassword = "Users.Error.SamePassword";
    public const string LastAdmin = "Users.Error.LastAdmin";
    public const string CannotChangeSelf = "Users.Error.CannotChangeSelf";
    public const string SetupAlreadyDone = "Setup.Error.AlreadyDone";
}
