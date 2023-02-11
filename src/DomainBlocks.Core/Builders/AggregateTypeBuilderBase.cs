using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAggregateTypeBuilder
{
    IAggregateType AggregateType { get; }
}

public interface IIdentityBuilder<out TAggregate> : IKeyPrefixBuilder
{
    /// <summary>
    /// Specify a unique ID selector for the aggregate.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IKeyBuilder HasId(Func<TAggregate, string> idSelector);
}

public interface IKeyPrefixBuilder
{
    /// <summary>
    /// Specify a prefix to use for both stream and snapshot keys.
    /// </summary>
    void WithKeyPrefix(string prefix);
}

public interface IKeyBuilder : IKeyPrefixBuilder, ISnapshotKeyBuilder
{
    /// <summary>
    /// Specify a stream key selector.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    ISnapshotKeyBuilder WithStreamKey(Func<string, string> idToStreamKeySelector);
}

public interface ISnapshotKeyBuilder
{
    /// <summary>
    /// Specify a snapshot key selector.
    /// </summary>
    void WithSnapshotKey(Func<string, string> idToSnapshotKeySelector);
}

public abstract class AggregateTypeBuilderBase<TAggregate, TEventBase> :
    IAggregateTypeBuilder,
    IIdentityBuilder<TAggregate>,
    IKeyBuilder
{
    public IAggregateType<TAggregate> AggregateType
    {
        get
        {
            var aggregateType = AggregateTypeImpl;

            if (AutoEventTypeBuilder != null)
            {
                aggregateType = aggregateType.SetEventTypes(AutoEventTypeBuilder.Build());
            }

            // Any individually configured event types will override auto configured event types for a given CLR type.
            var eventTypes = EventTypeBuilders.Select(x => x.EventType);
            aggregateType = aggregateType.SetEventTypes(eventTypes);

            return aggregateType;
        }
    }

    IAggregateType IAggregateTypeBuilder.AggregateType => AggregateType;

    protected abstract AggregateTypeBase<TAggregate, TEventBase> AggregateTypeImpl { get; set; }

    internal IAutoAggregateEventTypeBuilder<TAggregate>? AutoEventTypeBuilder { get; set; }

    internal List<IAggregateEventTypeBuilder<TAggregate>> EventTypeBuilders { get; } = new();

    /// <summary>
    /// Specify a factory function for creating new instances of the aggregate type.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public IIdentityBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        AggregateTypeImpl = AggregateTypeImpl.SetFactory(factory);
        return this;
    }

    public IKeyBuilder HasId(Func<TAggregate, string> idSelector)
    {
        AggregateTypeImpl = AggregateTypeImpl.SetIdSelector(idSelector);
        return this;
    }

    public void WithKeyPrefix(string prefix)
    {
        AggregateTypeImpl = AggregateTypeImpl.SetKeyPrefix(prefix);
    }

    ISnapshotKeyBuilder IKeyBuilder.WithStreamKey(Func<string, string> idToStreamKeySelector)
    {
        AggregateTypeImpl = AggregateTypeImpl.SetIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    void ISnapshotKeyBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        AggregateTypeImpl = AggregateTypeImpl.SetIdToSnapshotKeySelector(idToSnapshotKeySelector);
    }

    /// <summary>
    /// Finds events deriving from type <see cref="TEventBase"/> in the specified assembly, and adds them to the
    /// aggregate types.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    public ITypeFilterBuilder UseEventTypesFrom(Assembly assembly)
    {
        var builder = new AssemblyAggregateEventTypeBuilder<TAggregate, TEventBase>(assembly);
        AutoEventTypeBuilder = builder;
        return builder;
    }
}