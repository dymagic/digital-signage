namespace Signage.Web;

/// <summary>App settings under the <c>Signage</c> section (env vars <c>SIGNAGE__*</c>, see ARCHITECTURE §5).</summary>
public sealed class SignageOptions
{
    public const string SectionName = "Signage";

    /// <summary>Apply EF Core migrations at start-up (R-60). Default on.</summary>
    public bool MigrateOnStartup { get; set; } = true;

    /// <summary>Create demo content at start-up (empty database only). Always on in Development.</summary>
    public bool SeedDemo { get; set; }

    /// <summary>Optional first admin, created at start-up only when there are no users (R-51).</summary>
    public BootstrapOptions Bootstrap { get; set; } = new();
}

public sealed class BootstrapOptions
{
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}
