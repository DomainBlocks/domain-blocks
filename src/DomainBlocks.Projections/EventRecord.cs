using System.Collections.Generic;

namespace DomainBlocks.Projections;

public class EventRecord<TEvent> : IEventRecord<TEvent>
{
    public EventRecord(TEvent @event, IReadOnlyDictionary<string, string> metadata)
    {
        Event = @event;
        Metadata = metadata;
    }

    public TEvent Event { get; }
    object IEventRecord.Event => Event;
    public IReadOnlyDictionary<string, string> Metadata { get; }
}