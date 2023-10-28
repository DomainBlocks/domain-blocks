using DomainBlocks.Core.Subscriptions.Concurrency;

namespace DomainBlocks.Core.Subscriptions;

public partial class EventStreamSubscription<TEvent, TPosition>
{
    private sealed class ConsumerSessionGroup : IDisposable
    {
        private readonly IReadOnlyDictionary<Guid, ConsumerSession> _sessions;

        public ConsumerSessionGroup(
            IEnumerable<IEventStreamConsumer<TEvent, TPosition>> consumers,
            ArenaQueue<Notification> queue)
        {
            _sessions = consumers.Select(x => new ConsumerSession(x, queue)).ToDictionary(x => x.Id);
        }

        public void Dispose()
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
        }

        public async Task<TPosition?> NotifyStarting(CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyStarting(cancellationToken));
            var startPositions = await Task.WhenAll(tasks);
            return startPositions.Min();
        }

        public async Task NotifyCatchingUp(CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyCatchingUp(cancellationToken));
            await Task.WhenAll(tasks);

            foreach (var session in _sessions.Values)
            {
                session.ResetCheckpointTimer();
            }
        }

        public Task NotifyEvent(TEvent @event, TPosition position, CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifyEvent(@event, position, cancellationToken));
            return Task.WhenAll(tasks);
        }

        public async Task NotifyLive(CancellationToken cancellationToken)
        {
            var notifyCheckpointTasks = _sessions.Values.Select(x => x.NotifyCheckpoint(cancellationToken));
            await Task.WhenAll(notifyCheckpointTasks);

            var notifyLiveTasks = _sessions.Values.Select(x => x.NotifyLive(cancellationToken));
            await Task.WhenAll(notifyLiveTasks);

            foreach (var session in _sessions.Values)
            {
                session.ResetCheckpointTimer();
            }
        }

        public Task NotifySubscriptionDropped(
            SubscriptionDroppedReason reason, Exception? exception, CancellationToken cancellationToken)
        {
            var tasks = _sessions.Values.Select(x => x.NotifySubscriptionDropped(reason, exception, cancellationToken));
            return Task.WhenAll(tasks);
        }

        public async Task NotifyCheckpointTimerElapsed(Guid sessionId, CancellationToken cancellationToken)
        {
            var session = _sessions[sessionId];
            await session.NotifyCheckpoint(cancellationToken);
            session.ResetCheckpointTimer();
        }
    }
}