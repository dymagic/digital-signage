using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Signage.Web.Features.Health;

/// <summary>
/// <c>/healthz</c> is liveness (the process answers; no dependencies are checked) and
/// <c>/readyz</c> is readiness (the database is reachable). See R-60.
/// </summary>
internal static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddSignageHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    public static IEndpointRouteBuilder MapSignageHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return endpoints;
    }
}
