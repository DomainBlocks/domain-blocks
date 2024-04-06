namespace DomainBlocks.V1.Abstractions.Subscriptions;

public abstract class SubscriptionMessage
{
    public class EventReceived : SubscriptionMessage
    {
        public EventReceived(StoredEventEntry eventEntry, SubscriptionPosition position)
        {
            EventEntry = eventEntry;
            Position = position;
        }

        public StoredEventEntry EventEntry { get; }
        public SubscriptionPosition Position { get; }
    }

    public class CaughtUp : SubscriptionMessage
    {
        public static readonly CaughtUp Instance = new();
    }

    public class FellBehind : SubscriptionMessage
    {
        public static readonly FellBehind Instance = new();
    }

    public class SubscriptionDropped : SubscriptionMessage
    {
        public SubscriptionDropped(Exception? exception)
        {
            Exception = exception;
        }

        public Exception? Exception { get; }
    }
}