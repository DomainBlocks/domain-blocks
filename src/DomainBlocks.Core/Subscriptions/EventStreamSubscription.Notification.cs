namespace DomainBlocks.Core.Subscriptions;

public partial class EventStreamSubscription<TEvent, TPosition>
{
    private enum NotificationType
    {
        CatchingUp,
        Event,
        Live,
        SubscriptionDropped,
        CheckpointTimerElapsed
    }

    // ReSharper disable once ClassNeverInstantiated.Local - instantiated by ArenaQueue
    private sealed class Notification
    {
        public NotificationType NotificationType { get; private set; }
        public TEvent? Event { get; private set; }
        public TPosition? Position { get; private set; }
        public SubscriptionDroppedReason? SubscriptionDroppedReason { get; private set; }
        public Exception? Exception { get; private set; }
        public IEventStreamConsumer<TEvent, TPosition>? Consumer { get; private set; }

        public void SetCatchingUp()
        {
            Clear();
            NotificationType = NotificationType.CatchingUp;
        }

        public void SetEvent(TEvent @event, TPosition position)
        {
            Clear();
            NotificationType = NotificationType.Event;
            Event = @event;
            Position = position;
        }

        public void SetLive()
        {
            Clear();
            NotificationType = NotificationType.Live;
        }

        public void SetSubscriptionDropped(SubscriptionDroppedReason? reason, Exception? exception)
        {
            Clear();
            NotificationType = NotificationType.SubscriptionDropped;
            SubscriptionDroppedReason = reason;
            Exception = exception;
        }

        public void SetCheckpointTimerElapsed(IEventStreamConsumer<TEvent, TPosition> consumer)
        {
            Clear();
            NotificationType = NotificationType.CheckpointTimerElapsed;
            Consumer = consumer;
        }

        private void Clear()
        {
            Event = default;
            Position = null;
            SubscriptionDroppedReason = null;
            Exception = null;
            Consumer = null;
        }
    }
}