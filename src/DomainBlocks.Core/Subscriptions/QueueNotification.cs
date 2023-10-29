namespace DomainBlocks.Core.Subscriptions;

internal sealed class QueueNotification<TEvent, TPosition> where TPosition : struct
{
    public QueueNotificationType NotificationType { get; private set; }
    public TEvent? Event { get; private set; }
    public TPosition? Position { get; private set; }
    public SubscriptionDroppedReason? SubscriptionDroppedReason { get; private set; }
    public Exception? Exception { get; private set; }
    public Guid? ConsumerSessionId { get; private set; }

    public void SetCatchingUp()
    {
        Clear();
        NotificationType = QueueNotificationType.CatchingUp;
    }

    public void SetEvent(TEvent @event, TPosition position)
    {
        Clear();
        NotificationType = QueueNotificationType.Event;
        Event = @event;
        Position = position;
    }

    public void SetLive()
    {
        Clear();
        NotificationType = QueueNotificationType.Live;
    }

    public void SetSubscriptionDropped(SubscriptionDroppedReason? reason, Exception? exception)
    {
        Clear();
        NotificationType = QueueNotificationType.SubscriptionDropped;
        SubscriptionDroppedReason = reason;
        Exception = exception;
    }

    public void SetCheckpointTimerElapsed(Guid consumerSessionId)
    {
        Clear();
        NotificationType = QueueNotificationType.CheckpointTimerElapsed;
        ConsumerSessionId = consumerSessionId;
    }

    private void Clear()
    {
        Event = default;
        Position = null;
        SubscriptionDroppedReason = null;
        Exception = null;
        ConsumerSessionId = null;
    }
}