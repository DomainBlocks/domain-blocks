using System.Collections.Generic;

namespace DomainBlocks.Projections.New;

public interface IEventRecord
{
    object Event { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}

public interface IEventRecord<out TEvent> : IEventRecord
{
    new TEvent Event { get; }
}

public static class EventRecordExtensions
{
    public static IEventRecord<TEvent> Cast<TEvent>(this IEventRecord source)
    {
        return new EventRecord<TEvent>((TEvent)source.Event, source.Metadata);
    }
}