using System;
using System.Collections.Generic;
using System.Threading;

namespace DomainBlocks.Projections.New;

public class EventHandlingContext<TResource>
{
    internal EventHandlingContext(CatchUpSubscriptionStatus subscriptionStatus, TResource resource)
    {
        SubscriptionStatus = subscriptionStatus;
        Resource = resource;
    }

    public CatchUpSubscriptionStatus SubscriptionStatus { get; }
    public TResource Resource { get; }
    public CancellationToken CancellationToken { get; internal set; }
    public IReadOnlyDictionary<string, string> Metadata { get; internal set; }

    public void DoOnSaved(Action<TResource> onSaved)
    {
    }
}