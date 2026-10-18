using System.Text.Json;

namespace Signage.Web.Features.Events;

/// <summary>
/// An event for players (ARCHITECTURE §4). <see cref="Screens"/> null means every screen. Sent between replicas as the
/// JSON payload of <c>NOTIFY signage_events</c>, and to players as a server-sent event named <see cref="Kind"/>.
/// </summary>
public sealed record ScreenEvent(string Kind, IReadOnlyList<Guid>? Screens = null, int? Seconds = null)
{
    /// <summary>The screen's manifest may have changed: fetch it now (R-25).</summary>
    public const string ManifestChanged = "manifest-changed";

    /// <summary>Show the red identify border and the name for <see cref="Seconds"/> (R-31).</summary>
    public const string Identify = "identify";

    /// <summary>Show the name for <see cref="Seconds"/>, without the border (R-32).</summary>
    public const string IdentifyAll = "identify-all";

    /// <summary>How long identification lasts (PLAYER_SPEC §7).</summary>
    public const int IdentifySeconds = 30;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScreenEvent AllManifestsChanged { get; } = new(ManifestChanged);

    public bool IsFor(Guid screenId) => Screens is null || Screens.Contains(screenId);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The SSE data line: what the player needs besides the event name.</summary>
    public string DataJson() => Seconds is { } seconds ? $"{{\"seconds\":{seconds}}}" : "{}";

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
