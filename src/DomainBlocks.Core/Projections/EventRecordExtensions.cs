namespace DomainBlocks.Core.Projections;

public static class EventRecordExtensions
{
    public static IEventRecord<TEvent> Cast<TEvent>(this IEventRecord source)
    {
        return new EventRecord<TEvent>((TEvent)source.Event, source.Metadata);
    }
}