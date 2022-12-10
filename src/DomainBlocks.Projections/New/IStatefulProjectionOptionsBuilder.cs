using System;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Projections.New;

public interface IStatefulProjectionOptionsBuilder<out TState>
{
    IStatefulProjectionOptionsBuilder<TState> OnInitializing(Func<TState, CancellationToken, Task> onInitializing);

    IStatefulProjectionOptionsBuilder<TState> OnSubscribing(
        Func<TState, CancellationToken, Task<IStreamPosition>> onSubscribing);

    IStatefulProjectionOptionsBuilder<TState> OnSave(Func<TState, IStreamPosition, CancellationToken, Task> onSave);

    IStatefulProjectionOptionsBuilder<TState> When<TEvent>(
        Func<TEvent, IEventHandlerContext<TState>, CancellationToken, Task> projection);

    IStatefulProjectionOptionsBuilder<TState> When<TEvent>(Action<TEvent, IEventHandlerContext<TState>> projection);
}