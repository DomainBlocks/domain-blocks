namespace DomainBlocks.EventStore.Abstractions.Events;

public interface IReadEvent<out TPayload> where TPayload : notnull
{
    CommittedEventHeader Header { get; }
    TPayload Payload { get; }
}