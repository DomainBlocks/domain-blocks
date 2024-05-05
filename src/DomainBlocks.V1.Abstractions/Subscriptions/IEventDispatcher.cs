namespace DomainBlocks.V1.Abstractions.Subscriptions;

public interface IEventDispatcher
{
    Task DispatchAsync(IEventWrapper @event);
}