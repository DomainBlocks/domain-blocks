using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public abstract class ReadModelProjectionBase<TView> : IReadModelProjection<TView>
{
    private static readonly Dictionary<Type, Func<TView, object, CancellationToken, Task<TView>>> EventAppliers = new();

    public virtual Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public abstract Task<TView> GetViewAsync(CancellationToken cancellationToken);

    public Task<TView> ApplyEventAsync(TView view, object @event, CancellationToken cancellationToken)
    {
        var eventType = @event.GetType();

        if (EventAppliers.TryGetValue(eventType, out var eventApplier))
        {
            return eventApplier(view, @event, cancellationToken);
        }

        return Task.FromResult(view);
    }

    public virtual Task<GlobalPosition?> OnLoadCheckpointAsync(TView view, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GlobalPosition?());
    }

    public virtual Task OnSaveCheckpointAsync(TView view, GlobalPosition position, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected void When<TEvent>(Func<TView, TEvent, CancellationToken, Task<TView>> eventApplier)
    {
        EventAppliers.Add(typeof(TEvent), (v, e, ct) => eventApplier(v, (TEvent)e, ct));
    }

    protected void When<TEvent>(Func<TView, TEvent, CancellationToken, Task> eventApplier)
    {
        EventAppliers.Add(typeof(TEvent), async (v, e, ct) =>
        {
            await eventApplier(v, (TEvent)e, ct);
            return v;
        });
    }
}