using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Signage.IntegrationTests.Infrastructure.PostgresFixture))]

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>One real PostgreSQL container shared by every integration test in the assembly.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
