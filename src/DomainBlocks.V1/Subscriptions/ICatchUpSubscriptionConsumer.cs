using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public interface ICatchUpSubscriptionConsumer : ISubscriptionConsumer
{
    Task OnCaughtUp(CancellationToken cancellationToken);
    Task OnFellBehind(CancellationToken cancellationToken);
    Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken);
    Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken);
}