using DomainBlocks.V1.Abstractions;

namespace DomainBlocks.V1.Subscriptions;

public abstract class CatchUpSubscriptionConsumerBase : ICatchUpSubscriptionConsumer
{
    private static readonly Dictionary<Type, Func<object, CancellationToken, Task>> EventHandlers = new();

    public virtual Task OnInitializing(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnSubscribing(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnSubscriptionDropped(Exception? exception, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    Task ISubscriptionConsumer.OnEvent(ReadEvent readEvent, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnCaughtUp(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task OnFellBehind(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public virtual Task<GlobalPosition?> OnLoadCheckpointAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new GlobalPosition?());
    }

    public virtual Task OnSaveCheckpointAsync(GlobalPosition position, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected void When<TEvent>(Func<TEvent, CancellationToken, Task> eventApplier)
    {
        EventHandlers.Add(typeof(TEvent), (e, ct) => eventApplier((TEvent)e, ct));
    }
}