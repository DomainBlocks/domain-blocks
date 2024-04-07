namespace DomainBlocks.V1.Subscriptions;

public enum EventStreamConsumerSessionStatus
{
    Uninitialized,
    Stopped,
    Started,
    Faulted
}