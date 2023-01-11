namespace DomainBlocks.Core.Projections.Experimental;

public static class EventError
{
    public static EventError<TRawEvent, TPosition> Create<TRawEvent, TPosition>(
        TRawEvent rawEvent,
        TPosition position,
        object deserializedEvent,
        IReadOnlyDictionary<string, string> deserializedMetadata,
        Exception exception) => new(rawEvent, position, deserializedEvent, deserializedMetadata, exception);
}

public class EventError<TRawEvent, TPosition>
{
    public EventError(
        TRawEvent rawEvent,
        TPosition position,
        object deserializedEvent,
        IReadOnlyDictionary<string, string> deserializedMetadata,
        Exception exception)
    {
        RawEvent = rawEvent;
        Position = position;
        DeserializedEvent = deserializedEvent;
        DeserializedMetadata = deserializedMetadata;
        Exception = exception;
    }

    public TRawEvent RawEvent { get; }
    public TPosition Position { get; }
    public object DeserializedEvent { get; }
    public IReadOnlyDictionary<string, string> DeserializedMetadata { get; }
    public Exception Exception { get; }
}