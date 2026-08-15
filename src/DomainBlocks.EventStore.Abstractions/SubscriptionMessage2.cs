namespace DomainBlocks.EventStore.Abstractions;

public abstract class SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull

{
    private SubscriptionMessage2()
    {
    }

    public sealed class EventReceived(ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos> @event) :
        SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>
    {
        public ReadEvent2<TEvent, TStreamId, TStreamPos, TLogPos> Event { get; } = @event;
    }

    public sealed class CaughtUp : SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>;

    public sealed class FellBehind : SubscriptionMessage2<TEvent, TStreamId, TStreamPos, TLogPos>;
}