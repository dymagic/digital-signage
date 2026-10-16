using System.Net;
using Signage.IntegrationTests.Infrastructure;

namespace Signage.IntegrationTests.Health;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Healthz_returns_200()
    {
        await using var factory = new SignageWebFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readyz_returns_200_when_database_is_reachable()
    {
        await using var factory = new SignageWebFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/readyz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_returns_503_but_healthz_stays_200_when_database_is_unreachable()
    {
        await using var factory = new SignageWebFactory("Host=127.0.0.1;Port=1;Username=x;Password=x;Timeout=2");
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var ready = await client.GetAsync(new Uri("/readyz", UriKind.Relative), ct);
        var live = await client.GetAsync(new Uri("/healthz", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public void Startup_fails_with_a_clear_message_when_the_connection_string_is_missing()
    {
        using var factory = new SignageWebFactory("");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings__Default", ex.Message, StringComparison.Ordinal);
    }
}
