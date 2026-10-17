using System.Text.RegularExpressions;
using Signage.Web.Features.Slides;

namespace Signage.UnitTests.Slides;

public partial class HtmlSlideDocumentTests
{
    [Fact]
    public void The_admin_preview_and_the_player_use_the_same_base_style()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Signage.sln")))
        {
            dir = dir.Parent;
        }

        var js = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Signage.Web", "wwwroot", "screen", "slides", "html.js"));
        var match = BaseStyleInJs().Match(js);

        Assert.True(match.Success, "BASE_STYLE not found in html.js");
        Assert.Equal(HtmlSlideDocument.BaseStyle, match.Groups[1].Value);
    }

    [Fact]
    public void The_document_puts_the_base_style_before_the_editors_html()
    {
        var doc = HtmlSlideDocument.Build("<h1>Hi</h1>");

        Assert.StartsWith("<!DOCTYPE html><meta charset=\"utf-8\"><style>", doc, StringComparison.Ordinal);
        Assert.EndsWith("</style><h1>Hi</h1>", doc, StringComparison.Ordinal);
    }

    [GeneratedRegex("export const BASE_STYLE =\\s*\"([^\"]+)\";")]
    private static partial Regex BaseStyleInJs();
}
