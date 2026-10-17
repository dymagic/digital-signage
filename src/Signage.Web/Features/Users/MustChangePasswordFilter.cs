using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Signage.Web.Features.Users;

/// <summary>New and reset users can only reach the change-password page (and log out) until they set their own password (R-52).</summary>
internal sealed class MustChangePasswordFilter : IAsyncPageFilter
{
    public const string ChangePasswordPage = "/Admin/Account/Password";

    private static readonly HashSet<string> AllowedPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ChangePasswordPage, "/Admin/Logout", "/Admin/Login", "/Error",
    };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var page = context.ActionDescriptor.ViewEnginePath;
        if (context.HttpContext.User.MustChangePassword() && !AllowedPages.Contains(page))
        {
            context.Result = new RedirectToPageResult(ChangePasswordPage);
            return;
        }

        await next().ConfigureAwait(false);
    }
}
