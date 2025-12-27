namespace DomainBlocks.EventStore.Abstractions.Events;

public interface IReadEvent<out TValue> where TValue : notnull
{
    CommittedEventHeader Header { get; }
    TValue Value { get; }
}