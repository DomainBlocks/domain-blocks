namespace DomainBlocks.EventStore.Abstractions;

public abstract class SubscriptionMessage
{
    private SubscriptionMessage()
    {
    }

    public sealed class EventReceived<TEvent>(ReadEvent<TEvent> @event) : SubscriptionMessage where TEvent : notnull
    {
        public ReadEvent<TEvent> Event { get; } = @event;
    }

    public sealed class CaughtUp : SubscriptionMessage;

    public sealed class FellBehind : SubscriptionMessage;
}