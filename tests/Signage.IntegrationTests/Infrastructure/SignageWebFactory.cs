using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Signage.Web.Data;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>Hosts the real app in-process against the given PostgreSQL connection string.</summary>
public sealed class SignageWebFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? timeProvider = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        if (timeProvider is not null)
        {
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(timeProvider)));
        }
    }

    /// <summary>Runs <paramref name="action"/> with a fresh scoped <see cref="SignageDbContext"/>.</summary>
    public async Task WithDbAsync(Func<SignageDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<SignageDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<SignageDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<SignageDbContext>());
    }
}
