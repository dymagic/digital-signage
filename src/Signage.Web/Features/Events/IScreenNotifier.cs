namespace Signage.Web.Features.Events;

/// <summary>
/// Tells players that something they show has changed (R-25). Features call it after saving, with the
/// screens whose sequence or settings may have changed. Over-notifying is fine: the manifest ETag stops
/// redundant re-renders (ARCHITECTURE §4). <see cref="PostgresScreenNotifier"/> sends them to every replica.
/// </summary>
public interface IScreenNotifier
{
    Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct);

    Task AllScreensChangedAsync(CancellationToken ct);

    /// <summary>Identify these screens (R-31), or every screen when null (R-32), for 30 s.</summary>
    Task IdentifyAsync(IReadOnlyCollection<Guid>? screenIds, CancellationToken ct);
}

/// <summary>Notifications that go nowhere (tests that don't care about events).</summary>
public sealed class NoOpScreenNotifier : IScreenNotifier
{
    public Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct) => Task.CompletedTask;

    public Task AllScreensChangedAsync(CancellationToken ct) => Task.CompletedTask;

    public Task IdentifyAsync(IReadOnlyCollection<Guid>? screenIds, CancellationToken ct) => Task.CompletedTask;
}
