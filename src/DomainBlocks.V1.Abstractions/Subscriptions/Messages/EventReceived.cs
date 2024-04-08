namespace DomainBlocks.V1.Abstractions.Subscriptions.Messages;

public class EventReceived : ISubscriptionMessage
{
    public EventReceived(StoredEventRecord eventRecord, SubscriptionPosition position)
    {
        EventRecord = eventRecord;
        Position = position;
    }

    public StoredEventRecord EventRecord { get; }
    public SubscriptionPosition Position { get; }
}