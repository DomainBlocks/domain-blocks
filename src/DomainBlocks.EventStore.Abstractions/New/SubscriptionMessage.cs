namespace DomainBlocks.EventStore.Abstractions.New;

public abstract class SubscriptionMessage : ISubscriptionMessage
{
    private SubscriptionMessage()
    {
    }

    public sealed class EventReceived<TEvent>(TEvent @event) : SubscriptionMessage where TEvent : notnull
    {
        public TEvent Event { get; } = @event;
    }

    public sealed class CaughtUp : SubscriptionMessage;

    public sealed class FellBehind : SubscriptionMessage;
}