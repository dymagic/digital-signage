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

    public static Uri RequireBaseUrl()
    {
        Assert.SkipWhen(BaseUrl is null, "E2E tests run only via scripts/test-e2e.sh (SIGNAGE_E2E_BASEURL not set).");
        return BaseUrl!;
    }
}
