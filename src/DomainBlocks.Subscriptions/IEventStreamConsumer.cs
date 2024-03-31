using DomainBlocks.Abstractions;

namespace DomainBlocks.Subscriptions;

public interface IEventStreamConsumer
{
    Task OnInitializing(CancellationToken cancellationToken);
    Task OnSubscribing(CancellationToken cancellationToken);
    Task OnSubscriptionDropped(Exception? exception, CancellationToken cancellationToken);
    Task OnCaughtUp(CancellationToken cancellationToken);
    Task OnFellBehind(CancellationToken cancellationToken);
    Task OnEvent(ReadEvent readEvent, CancellationToken cancellationToken);
    Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken);
    Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken);
}