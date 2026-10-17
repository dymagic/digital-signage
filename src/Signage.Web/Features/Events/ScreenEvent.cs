using System.Text.Json;

namespace Signage.Web.Features.Events;

/// <summary>
/// An event for players (ARCHITECTURE §4). <see cref="Screens"/> null means every screen. Sent between replicas as the
/// JSON payload of <c>NOTIFY signage_events</c>, and to players as a server-sent event named <see cref="Kind"/>.
/// </summary>
public sealed record ScreenEvent(string Kind, IReadOnlyList<Guid>? Screens = null)
{
    /// <summary>The screen's manifest may have changed: fetch it now (R-25).</summary>
    public const string ManifestChanged = "manifest-changed";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScreenEvent AllManifestsChanged { get; } = new(ManifestChanged);

    public bool IsFor(Guid screenId) => Screens is null || Screens.Contains(screenId);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ScreenEvent? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ScreenEvent>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
