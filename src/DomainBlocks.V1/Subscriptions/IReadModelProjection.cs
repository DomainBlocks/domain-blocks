using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public interface IReadModelProjection<TView>
{
    Task OnInitializingAsync(CancellationToken cancellationToken);

    Task<TView> GetViewAsync(CancellationToken cancellationToken);

    Task<TView> ApplyEventAsync(TView view, object @event, CancellationToken cancellationToken);

    Task<GlobalPosition?> OnLoadCheckpointAsync(TView view, CancellationToken cancellationToken);

    Task OnSaveCheckpointAsync(TView view, GlobalPosition position, CancellationToken cancellationToken);
}