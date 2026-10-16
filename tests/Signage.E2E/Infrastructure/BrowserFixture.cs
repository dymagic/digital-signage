using Microsoft.Playwright;

[assembly: AssemblyFixture(typeof(Signage.E2E.Infrastructure.BrowserFixture))]

namespace Signage.E2E.Infrastructure;

/// <summary>
/// One Chromium instance for the whole run. Before handing it out, waits until the app under test
/// answers <c>/healthz</c>, because compose starts the app without an in-image health check.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("E2E environment not configured.");

    public async ValueTask InitializeAsync()
    {
        if (E2EEnvironment.BaseUrl is not { } baseUrl)
        {
            return;
        }

        await WaitForAppAsync(baseUrl);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }
        _playwright?.Dispose();
    }

    private static async Task WaitForAppAsync(Uri baseUrl)
    {
        using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(2) };
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var response = await http.GetAsync(new Uri("/healthz", UriKind.Relative));
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }

            if (waited.Elapsed > TimeSpan.FromSeconds(90))
            {
                throw new TimeoutException($"App at {baseUrl} did not become healthy within 90 s.");
            }
            await Task.Delay(500);
        }
    }
}
