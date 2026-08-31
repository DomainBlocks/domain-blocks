namespace DomainBlocks.EventStore.Abstractions;

public abstract class SubscriptionMessage
{
    private SubscriptionMessage()
    {
    }

    public static class Event
    {
        public static Event<TEvent> Create<TEvent>(TEvent @event) where TEvent : notnull => new(@event);
    }

    public sealed class Event<TEvent>(TEvent @event) : SubscriptionMessage where TEvent : notnull
    {
        public TEvent Value { get; } = @event;
    }

    public sealed class CaughtUp : SubscriptionMessage;

    public sealed class FellBehind : SubscriptionMessage;
}