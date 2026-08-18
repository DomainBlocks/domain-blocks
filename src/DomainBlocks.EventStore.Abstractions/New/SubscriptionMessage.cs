namespace DomainBlocks.EventStore.Abstractions.New;

public abstract class SubscriptionMessage
{
    private SubscriptionMessage()
    {
    }

    public sealed class Event<TEvent>(TEvent @event) : SubscriptionMessage where TEvent : notnull
    {
        public TEvent Value { get; } = @event;
    }

    public sealed class CaughtUp : SubscriptionMessage;

    public sealed class FellBehind : SubscriptionMessage;
}