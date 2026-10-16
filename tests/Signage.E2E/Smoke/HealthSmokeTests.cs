using Signage.E2E.Infrastructure;

namespace Signage.E2E.Smoke;

[Trait("Category", "Smoke")]
public class HealthSmokeTests(BrowserFixture fixture) : E2ETest(fixture)
{
    [Fact]
    public async Task Healthz_loads_in_the_browser()
    {
        var response = await Page.GotoAsync("/healthz");

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Equal("Healthy", await Page.InnerTextAsync("body"));
    }
}
