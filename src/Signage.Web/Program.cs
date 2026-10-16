using Signage.Web;
using Signage.Web.Data;
using Signage.Web.Features.Health;

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
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddSignageHealthChecks();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync().ConfigureAwait(false);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

app.UseStaticFiles();
app.UseRouting();

app.MapSignageHealthChecks();
app.MapRazorPages();

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point; public so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
