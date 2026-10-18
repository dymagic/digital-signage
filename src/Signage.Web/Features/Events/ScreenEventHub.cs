using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Signage.Web.Features.Events;

/// <summary>
/// The players connected to this replica's event stream, by screen. <see cref="ScreenEventListener"/> publishes what
/// arrives through Postgres, so every replica reaches its own players (R-63). Nothing here is shared between replicas.
/// </summary>
public sealed class ScreenEventHub
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    /// <summary>Starts receiving the events for a screen; dispose to stop.</summary>
    public Subscription Subscribe(Guid screenId)
    {
        var subscription = new Subscription(this, screenId);
        _subscriptions.TryAdd(subscription, 0);
        return subscription;
    }

    public int SubscriberCount => _subscriptions.Count;

    public void Publish(ScreenEvent screenEvent)
    {
        ArgumentNullException.ThrowIfNull(screenEvent);
        foreach (var subscription in _subscriptions.Keys)
        {
            if (screenEvent.IsFor(subscription.ScreenId))
            {
                subscription.Channel.Writer.TryWrite(screenEvent);
            }
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly ScreenEventHub _hub;

        internal Subscription(ScreenEventHub hub, Guid screenId)
        {
            _hub = hub;
            ScreenId = screenId;
        }

        public Guid ScreenId { get; }

        // A slow client loses old events, not memory: one pending "manifest changed" is as good as many.
        internal Channel<ScreenEvent> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<ScreenEvent>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public ChannelReader<ScreenEvent> Events => Channel.Reader;

        public void Dispose()
        {
            _hub._subscriptions.TryRemove(this, out _);
            Channel.Writer.TryComplete();
        }
    }
}
