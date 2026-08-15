namespace DomainBlocks.EventStore.Abstractions;

public sealed class ReadAllOptions
{
    public static readonly ReadAllOptions Default = new();

    public ReadPosition<LogSequenceNumber> Position { get; init; } = ReadPosition<LogSequenceNumber>.Start;

    public ReadDirection Direction { get; init; } = ReadDirection.Forward;

    public int? MaxCount { get; init; }

    public bool IncludeMetadata { get; init; } = true;
}