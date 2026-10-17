using Microsoft.Extensions.Options;

namespace Signage.Web.Features.Users;

/// <summary>
/// R-51: when <c>SIGNAGE__BOOTSTRAP__ADMINEMAIL</c> and <c>…ADMINPASSWORD</c> are set and there are no users,
/// creates that admin at start-up. They must change the password at first login, so the value in the
/// environment stops being a working password. Invalid values stop start-up with a clear message.
/// </summary>
internal static partial class AdminBootstrapper
{
    public static async Task BootstrapAdminAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var options = scope.ServiceProvider.GetRequiredService<IOptions<SignageOptions>>().Value.Bootstrap;
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminBootstrapper));
            if (string.IsNullOrWhiteSpace(options.AdminEmail) && string.IsNullOrEmpty(options.AdminPassword))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrEmpty(options.AdminPassword))
            {
                throw new InvalidOperationException("Set both SIGNAGE__BOOTSTRAP__ADMINEMAIL and SIGNAGE__BOOTSTRAP__ADMINPASSWORD, or neither.");
            }

            var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
            var result = await accounts.CreateFirstAdminAsync(options.AdminEmail, options.AdminEmail, options.AdminPassword, mustChangePassword: true, ct).ConfigureAwait(false);
            if (result.Succeeded)
            {
                LogBootstrapped(logger);
            }
            else if (result.ErrorKey == AccountErrors.SetupAlreadyDone)
            {
                LogSkipped(logger);
            }
            else
            {
                throw new InvalidOperationException(
                    $"SIGNAGE__BOOTSTRAP__ADMINEMAIL/ADMINPASSWORD are invalid ({result.ErrorKey}). " +
                    $"Use a valid email and a password of {PasswordPolicy.MinLength}–{PasswordPolicy.MaxLength} characters.");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created the bootstrap admin from SIGNAGE__BOOTSTRAP__*; they must change the password at first login.")]
    private static partial void LogBootstrapped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "SIGNAGE__BOOTSTRAP__* is set but users already exist; nothing to do. You can remove those variables.")]
    private static partial void LogSkipped(ILogger logger);
}
