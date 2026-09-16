namespace DomainBlocks.EventStore;

public interface IPosition<out TSelf> where TSelf : struct, IPosition<TSelf>
{
    ulong Value { get; }
}