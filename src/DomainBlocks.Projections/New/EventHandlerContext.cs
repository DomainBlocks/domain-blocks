using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public class EventHandlerContext<TResource> : IEventHandlerContext<TResource>
{
    private readonly List<Func<TResource, CancellationToken, Task>> _onSavedActions = new();

    internal EventHandlerContext(CatchUpSubscriptionStatus subscriptionStatus, TResource resource)
    {
        SubscriptionStatus = subscriptionStatus;
        Resource = resource;
    }

    public CatchUpSubscriptionStatus SubscriptionStatus { get; }
    public TResource Resource { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; internal set; }

    public void DoOnSaved(Action<TResource> onSaved)
    {
        DoOnSaved((x, _) =>
        {
            onSaved(x);
            return Task.CompletedTask;
        });
    }

    public void DoOnSaved(Func<TResource, CancellationToken, Task> onSaved)
    {
        _onSavedActions.Add(onSaved);
    }

    internal async Task RunOnSavedActions(CancellationToken cancellationToken)
    {
        foreach (var onSaved in _onSavedActions)
        {
            await onSaved(Resource, cancellationToken);
        }

        _onSavedActions.Clear();
    }
}