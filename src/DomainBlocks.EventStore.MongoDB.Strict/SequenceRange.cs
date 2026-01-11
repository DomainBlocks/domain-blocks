namespace DomainBlocks.EventStore.MongoDB.Strict;

internal readonly record struct SequenceRange(long Start, long Count)
{
    public long EndExclusive => Start + Count;
}