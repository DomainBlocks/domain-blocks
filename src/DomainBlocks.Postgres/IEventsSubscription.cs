namespace DomainBlocks.Postgres;

public interface IEventsSubscription
{
    IAsyncEnumerable<object> Subscribe(EventsSubscriptionOptions options, CancellationToken ct);
}