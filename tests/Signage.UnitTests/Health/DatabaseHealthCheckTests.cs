using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Signage.Web.Features.Health;

namespace Signage.UnitTests.Health;

public class DatabaseHealthCheckTests
{
    [Fact]
    public async Task Unreachable_database_reports_unhealthy_without_leaking_connection_details()
    {
        // Port 1 on loopback refuses connections immediately; no database is needed.
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=x;Password=secret-value;Timeout=2");
        var check = new DatabaseHealthCheck(dataSource);

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("secret-value", result.Description, StringComparison.Ordinal);
    }
}
