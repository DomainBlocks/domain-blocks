namespace DomainBlocks.V1.Abstractions;

public static class StreamMessage
{
    public class Event : IStreamMessage
    {
        public Event(ReadEvent readEvent)
        {
            ReadEvent = readEvent;
        }

        public ReadEvent ReadEvent { get; }
    }

    public class CaughtUp : IStreamMessage
    {
        public static readonly CaughtUp Instance = new();
    }

    public class FellBehind : IStreamMessage
    {
        public static readonly FellBehind Instance = new();
    }

    public class SubscriptionDropped : IStreamMessage
    {
        public SubscriptionDropped(Exception? exception)
        {
            Exception = exception;
        }

        public Exception? Exception { get; }
    }
}