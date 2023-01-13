namespace DomainBlocks.Postgres;

public record EventsSubscriptionOptions(
    string ConnectionString,
    string SlotName,
    string PublicationName
);