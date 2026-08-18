namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventSubscriptionBuilder<out TEvent, in TPos> where TEvent : notnull where TPos : notnull
{
    ISubscription FromStart();
    ISubscription From(TPos position);
    ISubscription After(TPos position);
    ISubscription FromLive();

    interface ISubscription
    {
        IAsyncEnumerable<TEvent> ToAsyncEnumerable();
        ISubscriptionMessages AsMessages();
    }

    interface ISubscriptionMessages
    {
        IAsyncEnumerable<ISubscriptionMessage> ToAsyncEnumerable();
    }
}