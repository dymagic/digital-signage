using Npgsql;
using Signage.Web.Features.Health;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is missing. Set ConnectionStrings__Default to a PostgreSQL connection string.");
}

builder.Services.AddNpgsqlDataSource(connectionString, dataSource =>
{
    // The chiseled runtime image has no Kerberos libraries. Npgsql would otherwise probe GSS encryption on
    // every new connection and log an error. Operators who need GSS can still set it explicitly.
    if (!connectionString.Contains("Gss Encryption Mode", StringComparison.OrdinalIgnoreCase)
        && !connectionString.Contains("GssEncryptionMode", StringComparison.OrdinalIgnoreCase))
    {
        dataSource.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
    }
});
builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddSignageHealthChecks();

var app = builder.Build();

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
