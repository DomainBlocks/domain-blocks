using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAggregateOptionsBuilder
{
    public IAggregateOptions Options { get; }
}

public interface IIdSelectorBuilder<out TAggregate>
{
    public IIdToStreamKeySelectorBuilder HasId(Func<TAggregate, string> idSelector);
}

public interface IIdToStreamKeySelectorBuilder
{
    public IIdToSnapshotKeySelectorBuilder WithStreamKey(Func<string, string> idToStreamKeySelector);
}

public interface IIdToSnapshotKeySelectorBuilder
{
    public void WithSnapshotKey(Func<string, string> idToSnapshotKeySelector);
}

public abstract class AggregateOptionsBuilderBase<TAggregate, TEventBase> :
    IAggregateOptionsBuilder,
    IIdSelectorBuilder<TAggregate>,
    IIdToStreamKeySelectorBuilder,
    IIdToSnapshotKeySelectorBuilder
    where TEventBase : class
{
    private readonly List<Func<IEnumerable<IEventOptions>>> _eventsOptionsFactories = new();
    
    public IAggregateOptions<TAggregate> Options
    {
        get
        {
            var eventsOptions = _eventsOptionsFactories.SelectMany(x => x());
            var options = OptionsImpl.WithEventsOptions(eventsOptions);
            return options;
        }
    }

    IAggregateOptions IAggregateOptionsBuilder.Options => Options;

    protected abstract AggregateOptionsBase<TAggregate, TEventBase> OptionsImpl { get; set; }

    /// <summary>
    /// Specify a factory function for creating new instances of the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IIdSelectorBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        OptionsImpl = OptionsImpl.WithFactory(factory);
        return this;
    }

    /// <summary>
    /// Specify a unique ID selector for the aggregate.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IIdToStreamKeySelectorBuilder IIdSelectorBuilder<TAggregate>.HasId(Func<TAggregate, string> idSelector)
    {
        OptionsImpl = OptionsImpl.WithIdSelector(idSelector);
        return this;
    }

    /// <summary>
    /// Specify a stream key selector.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IIdToSnapshotKeySelectorBuilder IIdToStreamKeySelectorBuilder.WithStreamKey(
        Func<string, string> idToStreamKeySelector)
    {
        OptionsImpl = OptionsImpl.WithIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    /// <summary>
    /// Specify a snapshot key selector.
    /// </summary>
    void IIdToSnapshotKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        OptionsImpl = OptionsImpl.WithIdToSnapshotKeySelector(idToSnapshotKeySelector);
    }

    /// <summary>
    /// Adds the given event type to the aggregate options.
    /// </summary>
    /// <returns>
    /// An object that can be used to further configure the event type.
    /// </returns>
    public EventOptionsBuilder<TEvent, TEventBase> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new EventOptionsBuilder<TEvent, TEventBase>();
        _eventsOptionsFactories.Add(GetOptions);
        return builder;

        IEnumerable<IEventOptions> GetOptions()
        {
            yield return builder.Options;
        }
    }

    /// <summary>
    /// Finds events deriving from type <see cref="TEventBase"/> in the specified assembly, and adds them to the
    /// aggregate options.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public AssemblyEventOptionsBuilder<TEventBase> UseEventTypesFrom(Assembly assembly)
    {
        var builder = new AssemblyEventOptionsBuilder<TEventBase>(assembly);
        _eventsOptionsFactories.Add(() => builder.Build());
        return builder;
    }
}