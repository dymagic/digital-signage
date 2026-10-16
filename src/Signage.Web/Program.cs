using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Signage.Web;
using Signage.Web.Data;
using Signage.Web.Features.Health;
using Signage.Web.Features.Settings;
using Signage.Web.Features.Users;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is missing. Set ConnectionStrings__Default to a PostgreSQL connection string.");
}

builder.Services.Configure<SignageOptions>(builder.Configuration.GetSection(SignageOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignageData(connectionString);
builder.Services.AddSignageAuthentication();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddSignageAdminUi();
builder.Services.AddProblemDetails();
builder.Services.AddSignageHealthChecks();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);
await app.Services.BootstrapAdminAsync().ConfigureAwait(false);

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
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapSignageHealthChecks();
app.MapGet("/", () => Results.Redirect("/admin")).ExcludeFromDescription();
app.MapRazorPages();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point; public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
