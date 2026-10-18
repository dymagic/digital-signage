using System.Net;

namespace Signage.Web.Features.Player;

/// <summary>
/// <c>GET /screen</c>: the player page, stamped with the player build it belongs to (<see cref="PlayerBuild"/>). When a manifest later reports a
/// different version, the player reloads to pick up the new code (R-26). Never cached.
/// </summary>
internal static class PlayerShell
{
    private const string Marker = "<meta name=\"signage-version\" content=\"\">";

    public static async Task<IResult> Serve(IWebHostEnvironment env, PlayerBuild build, HttpContext http, CancellationToken ct)
    {
        var file = env.WebRootFileProvider.GetFileInfo("screen/index.html");
        string html;
        await using (var stream = file.CreateReadStream())
        using (var reader = new StreamReader(stream))
        {
            html = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        http.Response.Headers.CacheControl = "no-cache";
        var stamped = html.Replace(Marker, $"<meta name=\"signage-version\" content=\"{WebUtility.HtmlEncode(build.Id)}\">", StringComparison.Ordinal);
        return Results.Content(stamped, "text/html; charset=utf-8");
    }
}
