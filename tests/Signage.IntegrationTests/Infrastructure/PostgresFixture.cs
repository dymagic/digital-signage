using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Signage.IntegrationTests.Infrastructure.PostgresFixture))]

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>
/// One real PostgreSQL container shared by every integration test in the assembly. Tests that touch data
/// call <see cref="CreateDatabaseAsync"/> to get their own empty database, so they never interfere.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    /// <summary>The container's default database (always exists, never migrated by tests).</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new, empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }
}
