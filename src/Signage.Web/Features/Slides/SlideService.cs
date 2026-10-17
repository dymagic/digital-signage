using Microsoft.EntityFrameworkCore;
using Signage.Web.Data;
using Signage.Web.Domain;

namespace Signage.Web.Features.Slides;

/// <summary>A slide in the library list, with how many playlists and screens use it.</summary>
public sealed record SlideListItem(Slide Slide, int PlaylistCount, int ScreenCount);

/// <summary>Where a slide is used, shown before it is deleted.</summary>
public sealed record SlideUsage(IReadOnlyList<string> Playlists, IReadOnlyList<string> Screens)
{
    public bool InUse => Playlists.Count > 0 || Screens.Count > 0;
}

/// <summary>The shared slide library (R-01–R-07). Editors and admins.</summary>
public sealed class SlideService(SignageDbContext db, TimeProvider time)
{
    public async Task<IReadOnlyList<SlideListItem>> ListAsync(string? search, CancellationToken ct)
    {
        var query = db.Slides.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(s => EF.Functions.ILike(s.Title, pattern, "\\"));
        }

        return await query
            .OrderBy(s => s.Title)
            .ThenBy(s => s.CreatedAt)
            .Select(s => new SlideListItem(
                s,
                db.PlaylistSlides.Where(ps => ps.SlideId == s.Id).Select(ps => ps.PlaylistId).Distinct().Count(),
                db.ScreenSlides.Where(ss => ss.SlideId == s.Id).Select(ss => ss.ScreenId).Distinct().Count()))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<Slide?> GetAsync(Guid id, CancellationToken ct) =>
        db.Slides.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Guid> CreateAsync(SlideType type, ValidSlide valid, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(valid);
        var slide = new Slide { Title = valid.Title, Type = type };
        Apply(slide, valid);
        db.Slides.Add(slide);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return slide.Id;
    }

    public async Task<OperationResult> UpdateAsync(Guid id, ValidSlide valid, uint version, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(valid);
        var slide = await db.Slides.SingleOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (slide is null)
        {
            return OperationResult.Fail(CommonErrors.NotFound);
        }

        db.Entry(slide).Property(s => s.Version).OriginalValue = version;
        Apply(slide, valid);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult.Fail(CommonErrors.Concurrency);
        }
        return OperationResult.Ok;
    }

    /// <summary>R-04: switch a slide on or off from the list, without a full edit.</summary>
    public async Task<OperationResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        // Bulk updates bypass TimestampInterceptor, so UpdatedAt is set here explicitly.
        var now = time.GetUtcNow();
        var updated = await db.Slides.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEnabled, enabled).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        return updated == 0 ? OperationResult.Fail(CommonErrors.NotFound) : OperationResult.Ok;
    }

    public async Task<SlideUsage> GetUsageAsync(Guid id, CancellationToken ct)
    {
        var playlists = await db.PlaylistSlides.Where(ps => ps.SlideId == id)
            .Select(ps => ps.Playlist.Name).Distinct().OrderBy(n => n).ToListAsync(ct).ConfigureAwait(false);
        var screens = await db.ScreenSlides.Where(ss => ss.SlideId == id)
            .Select(ss => ss.Screen.Name).Distinct().OrderBy(n => n).ToListAsync(ct).ConfigureAwait(false);
        return new SlideUsage(playlists, screens);
    }

    /// <summary>
    /// Deletes the slide and every link to it (playlist entries, prepended/appended slides), then closes the gaps
    /// those links leave so positions stay dense.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var playlistIds = await db.PlaylistSlides.Where(ps => ps.SlideId == id).Select(ps => ps.PlaylistId).Distinct().ToListAsync(ct).ConfigureAwait(false);
            var screenIds = await db.ScreenSlides.Where(ss => ss.SlideId == id).Select(ss => ss.ScreenId).Distinct().ToListAsync(ct).ConfigureAwait(false);
            var deleted = await db.Slides.Where(s => s.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            if (deleted == 0)
            {
                return OperationResult.Fail(CommonErrors.NotFound);
            }

            await db.RenumberPlaylistsAsync(playlistIds, ct).ConfigureAwait(false);
            await db.RenumberScreenSlidesAsync(screenIds, ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return OperationResult.Ok;
        }).ConfigureAwait(false);
    }

    private static void Apply(Slide slide, ValidSlide valid)
    {
        slide.Title = valid.Title;
        slide.Url = valid.Url;
        slide.Html = valid.Html;
        slide.DurationSeconds = valid.DurationSeconds;
        slide.PlayAudio = valid.PlayAudio;
        slide.ActiveFromUtc = valid.ActiveFromUtc;
        slide.ActiveUntilUtc = valid.ActiveUntilUtc;
        slide.IsEnabled = valid.IsEnabled;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}
