using System.Collections.Concurrent;
using Signage.Web.Features.Events;

namespace Signage.IntegrationTests.Infrastructure;

/// <summary>Records what features report to <see cref="IScreenNotifier"/>.</summary>
public sealed class RecordingScreenNotifier : IScreenNotifier
{
    private readonly ConcurrentQueue<IReadOnlyCollection<Guid>> _screens = new();
    private int _all;

    public IReadOnlyList<Guid> NotifiedScreens => _screens.SelectMany(s => s).Distinct().ToList();

    public int AllScreensNotifications => _all;

    public void Clear()
    {
        _screens.Clear();
        Interlocked.Exchange(ref _all, 0);
    }

    public Task ScreensChangedAsync(IReadOnlyCollection<Guid> screenIds, CancellationToken ct)
    {
        _screens.Enqueue(screenIds.ToList());
        return Task.CompletedTask;
    }

    public Task AllScreensChangedAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _all);
        return Task.CompletedTask;
    }
}
