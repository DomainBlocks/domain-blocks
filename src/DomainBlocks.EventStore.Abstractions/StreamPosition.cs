namespace DomainBlocks.EventStore.Abstractions;

public readonly struct StreamPosition
{
    public static readonly StreamPosition Start = new(StreamVersion.None, StreamPositionKind.Start);
    public static readonly StreamPosition End = new(StreamVersion.None, StreamPositionKind.End);

    private StreamPosition(StreamVersion version, StreamPositionKind kind)
    {
        Version = version;
        Kind = kind;
    }

    public StreamVersion Version { get; }

    public StreamPositionKind Kind { get; }

    public bool IsStart => Kind == StreamPositionKind.Start;

    public bool IsEnd => Kind == StreamPositionKind.End;

    public bool IsSpecific => Kind == StreamPositionKind.Specific;

    public static StreamPosition At(StreamVersion version)
    {
        return version == StreamVersion.None ? Start : new StreamPosition(version, StreamPositionKind.Specific);
    }

    public override string ToString()
    {
        if (IsStart) return nameof(Start);
        if (IsEnd) return nameof(End);
        return Version.ToString();
    }
}