using DomainBlocks.Abstractions;

namespace DomainBlocks.Subscriptions;

public interface IEventStreamConsumer
{
    Task OnInitializing(CancellationToken cancellationToken);
    Task OnSubscribing(CancellationToken cancellationToken);
    Task OnSubscriptionDropped(CancellationToken cancellationToken);
    Task OnCaughtUp(CancellationToken cancellationToken);
    Task OnFellBehind(CancellationToken cancellationToken);
    Task OnEvent(ReadEvent readEvent, CancellationToken cancellationToken);
    Task<long?> OnLoadCheckpointAsync(CancellationToken cancellationToken);
    Task OnSaveCheckpointAsync(long position, CancellationToken cancellationToken);
}