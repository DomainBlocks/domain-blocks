namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

internal readonly record struct SequenceAllocation(long Start, long Count)
{
    public long EndExclusive => Start + Count;
}