using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>A device: keeps its own cookies (the screen token), talks to /api/player like player.js does.</summary>
public sealed class PlayerClient : IDisposable
{
    private readonly HttpClient _client;

    public PlayerClient(SignageWebFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    public HttpClient Http => _client;

    public sealed record Pairing(string Code, Guid PairingId, string DeviceSecret, DateTimeOffset ExpiresAt);

    public async Task<Pairing> StartPairingAsync()
    {
        using var response = await _client.PostAsync(new Uri("/api/player/pairing", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Pairing>(TestContext.Current.CancellationToken))!;
    }

    public async Task<HttpResponseMessage> PollAsync(Pairing pairing, string? secret = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/player/pairing/{pairing.PairingId}", UriKind.Relative));
        request.Headers.Add("X-Device-Secret", secret ?? pairing.DeviceSecret);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<string> PollStatusAsync(Pairing pairing)
    {
        using var response = await PollAsync(pairing);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("status").GetString()!;
    }

    public Task<HttpResponseMessage> GetManifestAsync() =>
        _client.GetAsync(new Uri("/api/player/manifest", UriKind.Relative), TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> ExchangeAsync(string token) =>
        _client.PostAsJsonAsync(new Uri("/api/player/session", UriKind.Relative), new { token }, TestContext.Current.CancellationToken);

    public void Dispose() => _client.Dispose();
}
