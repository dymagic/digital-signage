using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Signage.Web;
using Signage.Web.Features;

namespace Signage.UnitTests.Localization;

/// <summary>
/// R-64: every UI string comes from <c>SharedResource.resx</c>. This finds the keys the code uses
/// (<c>L["…"]</c>/<c>l["…"]</c>, data-annotation names and messages, status keys, and the error-key
/// constants) and checks each one exists, so a typo can't show a raw key to users.
/// </summary>
public partial class ResourceCoverageTests
{
    private static readonly string WebRoot = Path.Combine(RepoRoot(), "src", "Signage.Web");

    private static readonly HashSet<string> ResourceKeys = XDocument.Load(Path.Combine(WebRoot, "Resources", "SharedResource.resx"))
        .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_key_used_in_pages_and_code_exists_in_the_resource_file()
    {
        var files = Directory.EnumerateFiles(WebRoot, "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(WebRoot, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var missing = files
            .SelectMany(f =>
            {
                var text = File.ReadAllText(f);
                var direct = KeyUsage().Matches(text).Select(m => m.Groups["key"].Value);
                var ternaries = TernaryKeyUsage().Matches(text).SelectMany(m => new[] { m.Groups["a"].Value, m.Groups["b"].Value });
                return direct.Concat(ternaries).Select(key => (File: Path.GetFileName(f), Key: key));
            })
            .Where(u => !ResourceKeys.Contains(u.Key))
            .Select(u => $"{u.File}: {u.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_error_key_constant_exists_in_the_resource_file()
    {
        var constants = typeof(OperationResult).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.DoesNotContain(constants, k => !ResourceKeys.Contains(k));
    }

    [Fact]
    public void Role_names_used_by_the_users_list_exist()
    {
        foreach (var role in Enum.GetNames<Signage.Web.Domain.UserRole>())
        {
            Assert.Contains($"Users.Role.{role}", ResourceKeys);
        }
    }

    [Fact]
    public void Slide_type_texts_exist_for_every_type()
    {
        foreach (var type in Enum.GetValues<Signage.Web.Domain.SlideType>())
        {
            Assert.Contains($"Slides.Type.{type}", ResourceKeys);
            Assert.Contains($"Slides.New.{type}", ResourceKeys);
            if (type != Signage.Web.Domain.SlideType.Html)
            {
                Assert.Contains($"Slides.Hint.Url.{type}", ResourceKeys);
            }
        }
    }

    [Fact]
    public void Screen_texts_built_from_enums_exist()
    {
        foreach (var mode in Enum.GetNames<Signage.Web.Domain.ScreenMode>())
        {
            Assert.Contains($"Screens.Mode.{mode}", ResourceKeys);
        }

        foreach (var placement in Enum.GetNames<Signage.Web.Domain.SlidePlacement>())
        {
            Assert.Contains($"Screens.No{placement}", ResourceKeys);
            Assert.Contains($"Screens.Add{placement}", ResourceKeys);
        }
    }

    [Fact]
    public void The_shared_resource_is_found_at_runtime()
    {
        var name = typeof(SharedResource).Assembly.GetManifestResourceNames();
        Assert.Contains("Signage.Web.Resources.SharedResource.resources", name);
    }

    // L["Key"], l["Key", arg], ErrorMessage = "Key", Name = "Key", TempData["Status"] = "Key" — keys look like "Area.Thing".
    [GeneratedRegex("""(?:\b[Ll]\[\s*|ErrorMessage\s*=\s*|\bName\s*=\s*|TempData\["Status"\]\s*=\s*)"(?<key>[A-Z][A-Za-z]+(?:\.[A-Za-z]+)+)"(?!\s*\+)""")]
    private static partial Regex KeyUsage();

    // L[cond ? "A.Key" : "B.Key"]
    [GeneratedRegex("\\b[Ll]\\[[^\\]\"]*\\?\\s*\"(?<a>[A-Z][A-Za-z]+(?:\\.[A-Za-z]+)+)\"\\s*:\\s*\"(?<b>[A-Z][A-Za-z]+(?:\\.[A-Za-z]+)+)\"")]
    private static partial Regex TernaryKeyUsage();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Signage.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
