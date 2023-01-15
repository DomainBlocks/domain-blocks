namespace Projections.Playground.Npgsql;

public record EventsSubscriptionOptions(
    string ConnectionString,
    string SlotName,
    string PublicationName
);