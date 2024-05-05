using DomainBlocks.V1.Abstractions.Subscriptions;

namespace DomainBlocks.V1.Subscriptions;

public interface IEventHandler<TEvent>
{
    Task OnEventAsync(EventHandlerContext<TEvent> context);
}