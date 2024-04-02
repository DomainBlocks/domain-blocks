using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public interface ICatchUpSubscriptionConsumer : ISubscriptionConsumer
{
    Task OnCaughtUpAsync(CancellationToken cancellationToken);
    Task OnFellBehindAsync(CancellationToken cancellationToken);
    Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken);
    Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken);
}