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

    protected abstract AggregateOptionsBase<TAggregate, TEventBase> Options { get; set; }

    IAggregateOptions IAggregateOptionsBuilder.Options
    {
        get
        {
            var eventsOptions = _eventsOptionsFactories.SelectMany(x => x());
            var options = Options.WithEventsOptions(eventsOptions);
            return options;
        }
    }

    public IIdSelectorBuilder<TAggregate> InitialState(Func<TAggregate> factory)
    {
        Options = Options.WithFactory(factory);
        return this;
    }

    IIdToStreamKeySelectorBuilder IIdSelectorBuilder<TAggregate>.HasId(Func<TAggregate, string> idSelector)
    {
        Options = Options.WithIdSelector(idSelector);
        return this;
    }

    IIdToSnapshotKeySelectorBuilder IIdToStreamKeySelectorBuilder.WithStreamKey(
        Func<string, string> idToStreamKeySelector)
    {
        Options = Options.WithIdToStreamKeySelector(idToStreamKeySelector);
        return this;
    }

    void IIdToSnapshotKeySelectorBuilder.WithSnapshotKey(Func<string, string> idToSnapshotKeySelector)
    {
        Options = Options.WithIdToSnapshotKeySelector(idToSnapshotKeySelector);
    }

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

    public AssemblyEventOptionsBuilder<TEventBase> Events(Assembly assembly)
    {
        var builder = new AssemblyEventOptionsBuilder<TEventBase>(assembly);
        _eventsOptionsFactories.Add(() => builder.Build());
        return builder;
    }
}