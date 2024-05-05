namespace DomainBlocks.V1.Abstractions.Subscriptions;

public interface IEventWrapper
{
    object Event { get; }
    SubscriptionPosition Position { get; }
}

public interface IEventWrapper<out TEvent> : IEventWrapper where TEvent : notnull
{
    new TEvent Event { get; }
}