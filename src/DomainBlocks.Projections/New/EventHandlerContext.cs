using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class EventHandlerContext<TState> : IEventHandlerContext<TState>
{
    private readonly List<Func<TState, CancellationToken, Task>> _onSavedActions = new();

    internal EventHandlerContext(CatchUpSubscriptionStatus subscriptionStatus, TState state)
    {
        SubscriptionStatus = subscriptionStatus;
        State = state;
    }

    public CatchUpSubscriptionStatus SubscriptionStatus { get; }
    public TState State { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; internal set; }

    public void DoOnSaved(Action<TState> onSaved)
    {
        DoOnSaved((x, _) =>
        {
            onSaved(x);
            return Task.CompletedTask;
        });
    }

    public void DoOnSaved(Func<TState, CancellationToken, Task> onSaved)
    {
        _onSavedActions.Add(onSaved);
    }

    internal async Task RunOnSavedActions(CancellationToken cancellationToken)
    {
        foreach (var onSaved in _onSavedActions)
        {
            await onSaved(State, cancellationToken);
        }

        _onSavedActions.Clear();
    }
}