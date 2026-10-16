using Signage.E2E.Infrastructure;

namespace Signage.E2E.Smoke;

[Trait("Category", "Smoke")]
public class HealthSmokeTests(BrowserFixture fixture)
{
    [Fact]
    public async Task Healthz_loads_in_the_browser()
    {
        var baseUrl = E2EEnvironment.RequireBaseUrl();
        var page = await fixture.Browser.NewPageAsync(new() { BaseURL = baseUrl.ToString() });

        var response = await page.GotoAsync("/healthz");

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Equal("Healthy", await page.InnerTextAsync("body"));
        await page.CloseAsync();
    }
}
