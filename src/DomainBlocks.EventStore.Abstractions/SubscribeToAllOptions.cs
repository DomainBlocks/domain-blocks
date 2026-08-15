namespace DomainBlocks.EventStore.Abstractions;

public sealed class SubscribeToAllOptions
{
    public static readonly SubscribeToAllOptions Default = new();

    public ReadPosition<LogSequenceNumber> FromPosition { get; init; }

    public int LiveQueueCapacity { get; init; } = 1_000;
}