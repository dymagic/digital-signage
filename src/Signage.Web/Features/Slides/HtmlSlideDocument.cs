namespace Signage.Web.Features.Slides;

/// <summary>
/// The document an Html slide renders in (PLAYER_SPEC §5): a base stylesheet (no margins, black background, white
/// text) followed by the editor's HTML. The player builds the same document in <c>wwwroot/screen/slides/html.js</c>;
/// a unit test keeps <see cref="BaseStyle"/> identical in both, so the admin preview matches the screen.
/// </summary>
public static class HtmlSlideDocument
{
    public const string BaseStyle =
        "html,body{margin:0;padding:0;width:100%;height:100%;overflow:hidden;background:#000;color:#fff;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif}";

    public static string Build(string? html) => $"<!DOCTYPE html><meta charset=\"utf-8\"><style>{BaseStyle}</style>{html}";
}
