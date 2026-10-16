using System.Net;
using Npgsql;
using Signage.IntegrationTests.Infrastructure;

namespace Signage.IntegrationTests.Health;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Healthz_returns_200()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readyz_returns_200_when_database_is_reachable()
    {
        await using var factory = new SignageWebFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/readyz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_returns_503_but_healthz_stays_200_when_the_database_goes_away()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new SignageWebFactory(connectionString);
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        await DropDatabaseAsync(connectionString);

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

    private async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
