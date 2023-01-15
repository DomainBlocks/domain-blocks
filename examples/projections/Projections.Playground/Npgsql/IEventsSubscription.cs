namespace Projections.Playground.Npgsql;

public interface IEventsSubscription
{
    IAsyncEnumerable<object> Subscribe(EventsSubscriptionOptions options, CancellationToken ct);
}