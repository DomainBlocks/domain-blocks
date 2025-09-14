namespace DomainBlocks.EventStore.Abstractions;

public enum ReadStreamStatus
{
    Success,
    StreamNotFound,
    RangeEmpty
}