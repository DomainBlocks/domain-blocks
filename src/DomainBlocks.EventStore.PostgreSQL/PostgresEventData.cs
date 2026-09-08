namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The stored representation of an event's data: either a JSON document, held in a <c>jsonb</c> column, or raw bytes,
/// held in a <c>bytea</c> column.
/// </summary>
public readonly struct PostgresEventData
{
    private readonly string? _json;
    private readonly ReadOnlyMemory<byte> _bytes;

    private PostgresEventData(string? json, ReadOnlyMemory<byte> bytes, bool isBytes)
    {
        _json = json;
        _bytes = bytes;
        IsBytes = isBytes;
    }

    public static PostgresEventData FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new PostgresEventData(json, default, false);
    }

    public static PostgresEventData FromBytes(ReadOnlyMemory<byte> bytes)
    {
        return new PostgresEventData(null, bytes, true);
    }

    public bool IsJson => _json is not null;

    public bool IsBytes { get; }

    public string Json => _json ?? throw new InvalidOperationException("Event data is not JSON.");

    public ReadOnlyMemory<byte> Bytes =>
        IsBytes ? _bytes : throw new InvalidOperationException("Event data is not bytes.");

    public override string ToString()
    {
        if (IsJson)
            return _json!;

        return IsBytes ? $"{_bytes.Length} bytes" : "(none)";
    }
}
