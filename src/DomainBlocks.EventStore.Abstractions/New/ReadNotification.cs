namespace DomainBlocks.EventStore.Abstractions.New;

public abstract class ReadNotification<TEvent> where TEvent : notnull
{
    private ReadNotification()
    {
    }

    public sealed class EventReceived(TEvent @event) : ReadNotification<TEvent>
    {
        public TEvent Event { get; } = @event;
    }

    public sealed class CaughtUp : ReadNotification<TEvent>;

    public sealed class FellBehind : ReadNotification<TEvent>;
}