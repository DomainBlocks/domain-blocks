namespace DomainBlocks.EventStore.Abstractions.New;

public abstract record ReadDefinition<TPos> where TPos : notnull
{
    public sealed record Forward(ReadOrigin<TPos> Origin) : ReadDefinition<TPos>;

    public sealed record Backward(ReadOrigin<TPos> Origin) : ReadDefinition<TPos>;
}