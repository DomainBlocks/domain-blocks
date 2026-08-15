namespace DomainBlocks.EventStore.Abstractions;

public static class ReadEventContext2
{
    public static ReadEventContext2<TStreamPosition, TLogPosition> Create<TStreamPosition, TLogPosition>(
        string streamId,
        TStreamPosition streamPosition,
        TLogPosition logPosition,
        DateTime createdAtUtc)
        where TStreamPosition : notnull
        where TLogPosition : notnull
    {
        return new ReadEventContext2<TStreamPosition, TLogPosition>(
            streamId,
            streamPosition,
            logPosition,
            createdAtUtc);
    }
}

public readonly record struct ReadEventContext2<TStreamPos, TLogPos>(
    string StreamId,
    TStreamPos StreamPosition,
    TLogPos LogPosition,
    DateTime CreatedAtUtc)
    where TStreamPos : notnull
    where TLogPos : notnull;