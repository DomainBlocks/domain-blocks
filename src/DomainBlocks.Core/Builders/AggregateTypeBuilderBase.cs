using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DomainBlocks.Core.Builders;

public interface IAggregateTypeBuilder
{
    public IAggregateType Options { get; }
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

public abstract class AggregateTypeBuilderBase<TAggregate, TEventBase> :
    IAggregateTypeBuilder,
    IIdSelectorBuilder<TAggregate>,
    IIdToStreamKeySelectorBuilder,
    IIdToSnapshotKeySelectorBuilder
    where TEventBase : class
{
    private readonly List<Func<IEnumerable<IEventType>>> _eventTypeBuilders = new();
    
    protected abstract AggregateTypeBase<TAggregate, TEventBase> Options { get; set; }

    IAggregateType IAggregateTypeBuilder.Options
    {
        get
        {
            var eventTypeOptions = _eventTypeBuilders.SelectMany(x => x());
            var options = Options.WithEventTypes(eventTypeOptions);
            return options;
        }
    }

    public IIdSelectorBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        Options = Options.WithFactory(factory);
        return this;
    }

    IIdToStreamKeySelectorBuilder IIdSelectorBuilder<TAggregate>.HasId(Func<TAggregate, string> idSelector)
    {
        if (idSelector == null) throw new ArgumentNullException(nameof(idSelector));
        Options = Options.WithIdSelector(idSelector);
        return this;
    }

    IIdToSnapshotKeySelectorBuilder IIdToStreamKeySelectorBuilder.WithStreamKey(
        Func<string, string> idToStreamKeySelector)
    {
        if (idToStreamKeySelector == null) throw new ArgumentNullException(nameof(idToStreamKeySelector));
        Options = Options.WithIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    void IIdToSnapshotKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        if (idToSnapshotKeySelector == null) throw new ArgumentNullException(nameof(idToSnapshotKeySelector));
        Options = Options.WithIdToSnapshotKeySelector(idToSnapshotKeySelector);
    }

    public EventTypeBuilder<TEvent, TEventBase> Event<TEvent>() where TEvent : TEventBase
    {
        var builder = new EventTypeBuilder<TEvent, TEventBase>();
        _eventTypeBuilders.Add(() => new[] { builder.Options });
        return builder;
    }

    public AssemblyEventTypeBuilder<TEventBase> Events(Assembly assembly)
    {
        var builder = new AssemblyEventTypeBuilder<TEventBase>(assembly);
        _eventTypeBuilders.Add(() => builder.Build());
        return builder;
    }
}