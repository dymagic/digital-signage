using Microsoft.Playwright;

namespace Signage.E2E.Infrastructure;

/// <summary>
/// Base class for browser tests: each test gets its own browser context and page, with tracing on.
/// When a test fails, a screenshot and a Playwright trace are written to <c>artifacts/e2e/</c>
/// (open the trace with <c>pwsh playwright.ps1 show-trace</c> inside the runner image, or trace.playwright.dev).
/// Tests are skipped outside the E2E container (see <see cref="E2EEnvironment"/>).
/// </summary>
public abstract class E2ETest(BrowserFixture fixture) : IAsyncLifetime
{
    private IBrowserContext? _context;

    protected Uri BaseUrl { get; private set; } = null!;

    protected IPage Page { get; private set; } = null!;

    protected IBrowserContext Context => _context ?? throw new InvalidOperationException("Context not created.");

    /// <summary>The shared browser, for tests that need extra contexts (e.g. a device next to the admin).</summary>
    protected IBrowser Browser => fixture.Browser;

    /// <summary>When true, the test starts signed in as the E2E admin.</summary>
    protected virtual bool SignedInAsAdmin => false;

    /// <summary>Playwright tracing (screenshots + DOM snapshots) for failure artefacts. Measurements turn it off.</summary>
    protected virtual bool Tracing => true;

    public virtual async ValueTask InitializeAsync()
    {
        BaseUrl = E2EEnvironment.RequireBaseUrl();
        _context = await fixture.Browser.NewContextAsync(new()
        {
            BaseURL = BaseUrl.ToString(),
            ViewportSize = new() { Width = 1920, Height = 1080 },
            StorageState = SignedInAsAdmin ? await fixture.Admin.StorageStateAsync() : null,
        });
        if (Tracing)
        {
            await _context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });
        }
        Page = await _context.NewPageAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_context is null)
        {
            return;
        }

        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        if (failed)
        {
            var name = Sanitize(TestContext.Current.Test?.TestDisplayName ?? "unknown");
            Directory.CreateDirectory(E2EEnvironment.ArtifactsDir);
            try
            {
                await Page.ScreenshotAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, $"{name}.png") });
            }
            catch (PlaywrightException)
            {
                // The page may already be gone; the trace below still helps.
            }
            if (Tracing)
            {
                await _context.Tracing.StopAsync(new() { Path = Path.Combine(E2EEnvironment.ArtifactsDir, $"{name}.trace.zip") });
            }
        }
        else if (Tracing)
        {
            await _context.Tracing.StopAsync();
        }

        await _context.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Saves a full-page screenshot to <c>artifacts/e2e/screens/</c> for humans (README, reviews).</summary>
    protected async Task SaveScreenshotAsync(string name)
    {
        var dir = Path.Combine(E2EEnvironment.ArtifactsDir, "screens");
        Directory.CreateDirectory(dir);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{name}.png"), FullPage = true });
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat([' ', '(', ')', '"', ',']).ToHashSet();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
