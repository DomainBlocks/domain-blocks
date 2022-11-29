using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAggregateOptionsBuilder
{
    IAggregateOptions Options { get; }
}

public interface IAggregateOptionsBuilder<TAggregate> : IAggregateOptionsBuilder
{
    /// <summary>
    /// Specify a factory function for creating new instances of the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IAggregateOptionsBuilder<TAggregate> InitialState(Func<TAggregate> factory);

    /// <summary>
    /// Specify a unique ID selector for the aggregate.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IIdToKeySelectorBuilder HasId(Func<TAggregate, string> idSelector);
}

public interface IIdToKeySelectorBuilder
{
    /// <summary>
    /// Specify a stream key selector.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IIdToKeySelectorBuilder WithStreamKey(Func<string, string> idToStreamKeySelector);

    /// <summary>
    /// Specify a snapshot key selector.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IIdToKeySelectorBuilder WithSnapshotKey(Func<string, string> idToSnapshotKeySelector);
}

public abstract class AggregateOptionsBuilderBase<TAggregate, TEventBase> :
    IAggregateOptionsBuilder<TAggregate>,
    IIdToKeySelectorBuilder
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

    public IAggregateOptionsBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        OptionsImpl = OptionsImpl.WithFactory(factory);
        return this;
    }

    public IIdToKeySelectorBuilder HasId(Func<TAggregate, string> idSelector)
    {
        OptionsImpl = OptionsImpl.WithIdSelector(idSelector);
        return this;
    }

    IIdToKeySelectorBuilder IIdToKeySelectorBuilder.WithStreamKey(Func<string, string> idToStreamKeySelector)
    {
        OptionsImpl = OptionsImpl.WithIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    IIdToKeySelectorBuilder IIdToKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        OptionsImpl = OptionsImpl.WithIdToSnapshotKeySelector(idToSnapshotKeySelector);
        return this;
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