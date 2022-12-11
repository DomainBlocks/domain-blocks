using System.Collections.Generic;

namespace DomainBlocks.Projections.New;

public class EventRecord
{
    public EventRecord(object @event, IReadOnlyDictionary<string, string> metadata)
    {
        Event = @event;
        Metadata = metadata;
    }

    public object Event { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public EventRecord<T> Cast<T>() => new((T)Event, Metadata);
}

public class EventRecord<T>
{
    public EventRecord(T @event, IReadOnlyDictionary<string, string> metadata)
    {
        Event = @event;
        Metadata = metadata;
    }

    public T Event { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}