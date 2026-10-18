using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;
using Signage.Web.Features.Settings;

namespace Signage.Web.Features.Player;

public sealed record ManifestScreen(Guid Id, string Name);

public sealed record ManifestSettings(int DefaultDuration, bool ShowInfoOverlay, string TimeZone);

/// <summary>One slide as the player needs it. <see cref="DurationSeconds"/> is resolved (null for video: plays to the end).</summary>
public sealed record ManifestSlide(
    Guid Id,
    string Type,
    string? Url,
    string? Html,
    int? DurationSeconds,
    bool PlayAudio,
    DateTimeOffset? ActiveFromUtc,
    DateTimeOffset? ActiveUntilUtc);

/// <summary>Everything a player needs (ARCHITECTURE §4). The ETag covers all of it.</summary>
public sealed record Manifest(
    string AppVersion,
    ManifestScreen Screen,
    string Mode,
    string? SingleVideoUrl,
    ManifestSettings Settings,
    string? ObservedIp,
    IReadOnlyList<ManifestSlide> Slides);

public sealed class ManifestService(SignageDbContext db, SettingsService settings, TimeProvider time, PlayerBuild build)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The screen's manifest, or null if it doesn't exist. Slides keep their schedule windows (the player re-checks
    /// them, also offline), but slides whose window has already ended are left out.
    /// </summary>
    public async Task<Manifest?> BuildAsync(Guid screenId, string? observedIp, CancellationToken ct)
    {
        var screen = await db.LoadScreenForResolutionAsync(screenId, ct).ConfigureAwait(false);
        if (screen is null)
        {
            return null;
        }

        var current = await settings.GetAsync(ct).ConfigureAwait(false);
        var now = time.GetUtcNow();
        var slides = SequenceResolver.Resolve(screen, now, applySchedule: false)
            .Select(r => r.Slide)
            .Where(s => s.ActiveUntilUtc is null || s.ActiveUntilUtc > now)
            .Select(s => new ManifestSlide(
                s.Id,
                s.Type.ToString(),
                s.Url,
                s.Html,
                SlideDuration.Resolve(s, current.DefaultSlideDurationSeconds),
                s.PlayAudio,
                s.ActiveFromUtc,
                s.ActiveUntilUtc))
            .ToList();

        return new Manifest(
            build.Id,
            new ManifestScreen(screen.Id, screen.Name),
            screen.Mode.ToString(),
            screen.SingleVideoUrl,
            new ManifestSettings(current.DefaultSlideDurationSeconds, current.ShowInfoOverlay, current.TimeZoneId),
            observedIp,
            slides);
    }

    /// <summary>Serialises the manifest and computes its strong ETag (a hash of the exact bytes sent).</summary>
    public static (byte[] Body, string ETag) Serialize(Manifest manifest)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        return (body, $"\"{Convert.ToHexStringLower(SHA256.HashData(body))[..32]}\"");
    }
}
