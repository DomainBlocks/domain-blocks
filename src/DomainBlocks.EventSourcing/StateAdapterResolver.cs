using System.Collections.Frozen;

namespace DomainBlocks.EventSourcing;

public sealed class StateAdapterResolver<TEvent, TStreamId> : IEventSourcedStateAdapterResolver<TEvent, TStreamId>
    where TEvent : notnull
    where TStreamId : notnull
{
    private readonly FrozenDictionary<Type, IEventSourcedStateAdapter> _adapters;

    public StateAdapterResolver(IEnumerable<IEventSourcedStateAdapter> adapters)
    {
        var adapterDict = adapters.ToFrozenDictionary(x => x.StateType);

        if (!adapterDict.Values.All(x => x.GetType().HasInterface(typeof(IEventSourcedStateAdapter<,,>))))
        {
            throw new ArgumentException(
                $"State adapters must not implement '{typeof(IEventSourcedStateAdapter)}' directly.",
                nameof(adapters));
        }

        _adapters = adapterDict;
    }

    public IEventSourcedStateAdapter<TState, TEvent, TStreamId>? Resolve<TState>() where TState : notnull
    {
        return (IEventSourcedStateAdapter<TState, TEvent, TStreamId>?)_adapters.GetValueOrDefault(typeof(TState));
    }
}