using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public abstract class CatchUpSubscriptionConsumerBase : ICatchUpSubscriptionConsumer
{
    private readonly Dictionary<Type, Func<object, CancellationToken, Task>> _eventHandlers = new();

    public virtual Task OnInitializingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task OnSubscribingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task OnSubscriptionDroppedAsync(Exception? exception, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public virtual async Task OnEventAsync(object @event, CancellationToken cancellationToken)
    {
        var eventType = @event.GetType();

        if (_eventHandlers.TryGetValue(eventType, out var eventApplier))
        {
            await eventApplier(@event, cancellationToken);
        }
    }

    public virtual Task OnCaughtUpAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task OnFellBehindAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new GlobalPosition?());

    public virtual Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected void When<TEvent>(Func<TEvent, CancellationToken, Task> eventApplier)
    {
        _eventHandlers.Add(typeof(TEvent), (e, ct) => eventApplier((TEvent)e, ct));
    }
}