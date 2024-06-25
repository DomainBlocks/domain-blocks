namespace DomainBlocks.V1.Abstractions.Subscriptions;

public readonly record struct SubscriptionPosition(ulong Value)
{
    public StreamPosition AsStreamPosition() => new(Value);
    public GlobalPosition AsGlobalPosition() => new(Value);
}