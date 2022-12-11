using System.Collections.Generic;

namespace DomainBlocks.Projections.New;

public class EventRecord<TEvent> : IEventRecord<TEvent>
{
    public EventRecord(TEvent @event, IReadOnlyDictionary<string, string> metadata)
    {
        Event = @event;
        Metadata = metadata;
    }

    public TEvent Event { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}