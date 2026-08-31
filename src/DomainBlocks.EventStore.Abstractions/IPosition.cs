namespace DomainBlocks.EventStore.Abstractions;

public interface IPosition<out TSelf> where TSelf : struct, IPosition<TSelf>
{
    ulong Value { get; }
}