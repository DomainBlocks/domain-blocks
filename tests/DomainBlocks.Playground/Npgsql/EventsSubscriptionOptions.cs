namespace DomainBlocks.Playground.Npgsql;

public record EventsSubscriptionOptions(
    string ConnectionString,
    string SlotName,
    string PublicationName
);