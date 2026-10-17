using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Signage.Web;
using Signage.Web.Data;
using Signage.Web.Features.Demo;
using Signage.Web.Features.Events;
using Signage.Web.Features.Groups;
using Signage.Web.Features.Health;
using Signage.Web.Features.Hosting;
using Signage.Web.Features.Playlists;
using Signage.Web.Features.Player;
using Signage.Web.Features.Screens;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Slides;
using Signage.Web.Features.Users;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is missing. Set ConnectionStrings__Default to a PostgreSQL connection string.");
}

builder.Services.Configure<SignageOptions>(builder.Configuration.GetSection(SignageOptions.SectionName));
var forwardedHeaders = builder.Services.AddSignageForwardedHeaders(
    builder.Configuration.GetSection(SignageOptions.SectionName).Get<SignageOptions>()?.ForwardedHeaders ?? new());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignageData(connectionString);
builder.Services.AddSignageAuthentication();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SlideService>();
builder.Services.AddScoped<PlaylistService>();
builder.Services.AddScoped<ScreenService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddSingleton<IScreenNotifier, PostgresScreenNotifier>();
builder.Services.AddSingleton<ScreenEventHub>();
builder.Services.AddSingleton<ScreenEventListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScreenEventListener>());
builder.Services.AddSignagePlayerApi();
builder.Services.AddSignageAdminUi();
builder.Services.AddProblemDetails();
builder.Services.AddSignageHealthChecks();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
await app.Services.BootstrapAdminAsync().ConfigureAwait(false);
await app.Services.SeedDemoIfRequestedAsync().ConfigureAwait(false);

if (forwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(AdminUi.DefaultCulture),
    SupportedCultures = [new CultureInfo(AdminUi.DefaultCulture)],
    SupportedUICultures = [new CultureInfo(AdminUi.DefaultCulture)],
});
app.UseStaticFiles(new StaticFileOptions
{
    // The player has no build step or file hashes: let browsers revalidate its files so an update arrives (R-26).
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/screen", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapSignageHealthChecks();
app.MapSignagePlayerApi();
app.MapGet("/screen", PlayerShell.Serve).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/admin")).ExcludeFromDescription();
app.MapRazorPages();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point; public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
