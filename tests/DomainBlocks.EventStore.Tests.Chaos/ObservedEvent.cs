namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents an event as the event log holds it or as a subscriber received it. The operation and index identify the
/// event that the writer appended.
/// </summary>
internal sealed record ObservedEvent(
    LogPosition LogPosition,
    string StreamId,
    StreamPosition StreamPosition,
    long Operation,
    int Index)
{
    public static ObservedEvent From(ReadEvent<object, string, StreamPosition, LogPosition> readEvent)
    {
        var context = readEvent.Context;
        var chaosEvent = (ChaosEvent)readEvent.Payload;

        return new ObservedEvent(
            context.LogPosition,
            context.StreamId,
            context.StreamPosition,
            chaosEvent.Operation,
            chaosEvent.Index);
    }

    public override string ToString() =>
        $"event {Operation}.{Index} at log position {LogPosition} ({StreamId}@{StreamPosition})";
}