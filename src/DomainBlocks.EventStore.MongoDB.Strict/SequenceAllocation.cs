namespace DomainBlocks.EventStore.MongoDB.Strict;

internal readonly record struct SequenceAllocation(long Start, long Count)
{
    public long EndExclusive => Start + Count;
}