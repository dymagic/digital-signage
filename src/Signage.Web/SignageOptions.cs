namespace Signage.Web;

/// <summary>App settings under the <c>Signage</c> section (env vars <c>SIGNAGE__*</c>, see ARCHITECTURE §5).</summary>
public sealed class SignageOptions
{
    public const string SectionName = "Signage";

    /// <summary>Apply EF Core migrations at start-up (R-60). Default on.</summary>
    public bool MigrateOnStartup { get; set; } = true;
}
