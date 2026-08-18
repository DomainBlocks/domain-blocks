namespace DomainBlocks.EventStore.Abstractions.New;

public sealed record SubscriptionDefinition<TPos>(ReadOrigin<TPos> Origin, SubscriptionOptions Options)
    where TPos : notnull;