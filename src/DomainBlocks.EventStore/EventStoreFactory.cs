namespace DomainBlocks.EventStore;

public static class EventStoreFactory
{
    public static EventStore<TPayload> Create<TPayload>(EventStoreOptions<TPayload> options)
    {
        return new EventStore<TPayload>(options);
    }
}