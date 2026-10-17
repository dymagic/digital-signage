using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>
/// A browser-like client for the admin pages: keeps cookies, never follows redirects (tests assert them),
/// and posts forms with the antiforgery token taken from the page, like a real form submit.
/// </summary>
public sealed partial class AdminSession : IDisposable
{
    private readonly HttpClient _client;

    public AdminSession(SignageWebFactory factory, bool https = false)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
        });
    }

    public HttpClient Http => _client;

    public Task<HttpResponseMessage> GetAsync(string path) =>
        _client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    public async Task<string> GetHtmlAsync(string path)
    {
        using var response = await GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>GETs <paramref name="formPage"/> for its antiforgery token, then POSTs the fields to <paramref name="action"/> (default: the same page).</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string formPage, IDictionary<string, string> fields, string? action = null)
    {
        var token = await GetAntiforgeryTokenAsync(formPage);
        var all = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        using var content = new FormUrlEncodedContent(all);
        return await _client.PostAsync(new Uri(action ?? formPage, UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> PostWithoutTokenAsync(string action, IDictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await _client.PostAsync(new Uri(action, UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        await PostFormAsync("/admin/login", new Dictionary<string, string> { ["Input.Email"] = email, ["Input.Password"] = password });

    public async Task SignInSuccessfullyAsync(string email, string password)
    {
        using var response = await SignInAsync(email, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public async Task<string> GetAntiforgeryTokenAsync(string formPage)
    {
        var html = await GetHtmlAsync(formPage);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"No antiforgery token on {formPage}");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    public static string? Location(HttpResponseMessage response) => response.Headers.Location?.OriginalString;

    public void Dispose() => _client.Dispose();

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="(?<token>[^"]+)"|value="(?<token>[^"]+)" name="__RequestVerificationToken""")]
    private static partial Regex TokenRegex();
}
