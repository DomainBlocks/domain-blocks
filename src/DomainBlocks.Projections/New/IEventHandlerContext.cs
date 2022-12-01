using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IEventHandlerContext<out TState>
{
    CatchUpSubscriptionStatus SubscriptionStatus { get; }
    TState State { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
    void DoOnSaved(Action<TState> onSaved);
    void DoOnSaved(Func<TState, CancellationToken, Task> onSaved);
}