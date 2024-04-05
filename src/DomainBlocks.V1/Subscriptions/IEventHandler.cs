namespace DomainBlocks.V1.Subscriptions;

public interface IEventHandler<TEvent>
{
    Task OnEventAsync(EventHandlerContext<TEvent> context);
}