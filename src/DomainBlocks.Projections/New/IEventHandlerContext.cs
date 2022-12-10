using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IEventHandlerContext<out TResource>
{
    CatchUpSubscriptionStatus SubscriptionStatus { get; }
    TResource Resource { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
    void DoOnSaved(Action<TResource> onSaved);
    void DoOnSaved(Func<TResource, CancellationToken, Task> onSaved);
}