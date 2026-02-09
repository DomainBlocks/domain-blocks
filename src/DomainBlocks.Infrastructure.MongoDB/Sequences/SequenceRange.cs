namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public readonly record struct SequenceRange(long Start, long Count)
{
    public long EndExclusive => Start + Count;
}