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
{
    public IAggregateOptions<TAggregate> Options
    {
        get
        {
            var options = OptionsImpl;

            if (AutoEventOptionsBuilder != null)
            {
                options = options.WithEventsOptions(AutoEventOptionsBuilder.Build());
            }

            // Any individually configured event options will override auto configured event options for a given type.
            var eventsOptions = EventOptionsBuilders.Select(x => x.Options);
            options = options.WithEventsOptions(eventsOptions);

            return options;
        }
    }

    IAggregateOptions IAggregateOptionsBuilder.Options => Options;

    protected abstract AggregateOptionsBase<TAggregate, TEventBase> OptionsImpl { get; set; }

    internal IAutoEventOptionsBuilder<TAggregate> AutoEventOptionsBuilder { get; set; }

    internal List<IEventOptionsBuilder<TAggregate>> EventOptionsBuilders { get; } = new();

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
    /// Finds events deriving from type <see cref="TEventBase"/> in the specified assembly, and adds them to the
    /// aggregate options.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public AssemblyEventOptionsBuilder<TAggregate, TEventBase> UseEventTypesFrom(Assembly assembly)
    {
        var builder = new AssemblyEventOptionsBuilder<TAggregate, TEventBase>(assembly);
        AutoEventOptionsBuilder = builder;
        return builder;
    }
}