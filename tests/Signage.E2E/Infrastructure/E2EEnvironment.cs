namespace Signage.E2E.Infrastructure;

/// <summary>
/// E2E tests only run inside the Playwright container (scripts/test-e2e.sh), which sets
/// SIGNAGE_E2E_BASEURL. A plain host-side <c>dotnet test</c> skips them.
/// </summary>
public static class E2EEnvironment
{
    public static Uri? BaseUrl { get; } =
        Environment.GetEnvironmentVariable("SIGNAGE_E2E_BASEURL") is { Length: > 0 } url ? new Uri(url) : null;

    public static string ArtifactsDir { get; } =
        Environment.GetEnvironmentVariable("SIGNAGE_E2E_ARTIFACTS") is { Length: > 0 } dir ? dir : Path.Combine(AppContext.BaseDirectory, "artifacts");

    /// <summary>The nginx serving generated test media and the spike harness (docker-compose.e2e.yml).</summary>
    public static Uri MediaBaseUrl { get; } =
        new(Environment.GetEnvironmentVariable("SIGNAGE_E2E_MEDIA_BASEURL") is { Length: > 0 } url ? url : "http://testmedia");

    /// <summary>Spike measurements (WP-07) are long; they only run with SIGNAGE_E2E_SPIKE=1.</summary>
    public static bool SpikeEnabled { get; } = Environment.GetEnvironmentVariable("SIGNAGE_E2E_SPIKE") == "1";

    /// <summary>README screenshots (scripts/update-readme-screenshots.sh) only run with SIGNAGE_E2E_README=1, on a fresh database.</summary>
    public static bool ReadmeScreenshots { get; } = Environment.GetEnvironmentVariable("SIGNAGE_E2E_README") == "1";

    public static Uri RequireBaseUrl()
    {
        Assert.SkipWhen(BaseUrl is null, "E2E tests run only via scripts/test-e2e.sh (SIGNAGE_E2E_BASEURL not set).");
        return BaseUrl!;
    }
}
