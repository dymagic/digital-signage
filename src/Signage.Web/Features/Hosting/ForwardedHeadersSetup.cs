using System.Net;
using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.HttpOverrides;

namespace Signage.Web.Features.Hosting;

/// <summary>
/// Behind a reverse proxy or load balancer (ARCHITECTURE §5): with <c>SIGNAGE__FORWARDEDHEADERS__ENABLED</c> the app
/// takes the client IP and scheme from X-Forwarded-For/-Proto sent by the proxies in <c>KNOWNNETWORKS</c>. The host
/// header is never taken from the proxy. An invalid network stops start-up.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static bool AddSignageForwardedHeaders(this IServiceCollection services, ForwardedHeadersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return false;
        }

        var networks = ParseNetworks(settings.KnownNetworks);
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var network in networks)
            {
                o.KnownIPNetworks.Add(network);
            }
        });
        return true;
    }

    public static IReadOnlyList<IPNetwork> ParseNetworks(string? value)
    {
        var networks = new List<IPNetwork>();
        foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPNetwork.TryParse(part, out var network))
            {
                throw new InvalidOperationException(
                    $"SIGNAGE__FORWARDEDHEADERS__KNOWNNETWORKS: '{part}' is not a network in CIDR notation (e.g. 10.0.0.0/8).");
            }
            networks.Add(network);
        }
        return networks;
    }

    /// <summary>The client IP as the server sees it (R-30), IPv4-mapped IPv6 shown as plain IPv4.</summary>
    public static string? ObservedIp(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var ip = http.Connection.RemoteIpAddress;
        return ip is null ? null : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}
