using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Signage.Web.Features.Health;

/// <summary>Healthy when a trivial query against PostgreSQL succeeds.</summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // Don't echo connection details; the exception goes to the log only.
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
