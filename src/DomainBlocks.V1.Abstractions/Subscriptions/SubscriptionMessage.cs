namespace DomainBlocks.V1.Abstractions.Subscriptions;

public abstract class SubscriptionMessage
{
    public class Event : SubscriptionMessage
    {
        public Event(ReadEventRecord readEventRecord)
        {
            ReadEventRecord = readEventRecord;
        }

        public ReadEventRecord ReadEventRecord { get; }
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