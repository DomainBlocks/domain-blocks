namespace DomainBlocks.EventStore.MongoDB.Appender;

internal readonly record struct SequenceAllocation(long Start, long Count)
{
    public long EndExclusive => Start + Count;
}