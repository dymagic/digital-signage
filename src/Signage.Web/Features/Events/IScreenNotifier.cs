namespace Signage.Web.Features.Events;

/// <summary>
/// Tells players that something they show has changed (R-25). Features call it after saving, with the
/// screens whose sequence or settings may have changed. Over-notifying is fine: the manifest ETag stops
/// redundant re-renders (ARCHITECTURE §4). WP-10 replaces the no-op with Postgres LISTEN/NOTIFY + SSE.
/// </summary>
public interface IScreenNotifier
{
    Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct);

    Task AllScreensChangedAsync(CancellationToken ct);
}

/// <summary>Until live updates exist (WP-10), notifications go nowhere.</summary>
internal sealed class NoOpScreenNotifier : IScreenNotifier
{
    public Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct) => Task.CompletedTask;

    public Task AllScreensChangedAsync(CancellationToken ct) => Task.CompletedTask;
}
