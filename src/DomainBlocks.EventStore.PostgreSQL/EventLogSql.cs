namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The read queries over the event log. Reads use keyset pagination: every page selects rows strictly beyond a key
/// (a position) and the last row's key seeds the next page. Because positions are immutable and gap-free, pages are
/// exact and no row can be skipped or repeated.
/// </summary>
internal sealed class EventLogSql
{
    private const string Columns =
        "position, stream_id, stream_position, event_name, event_data, event_data_bytes, metadata, created_at";

    private const string ColumnsWithoutMetadata =
        "position, stream_id, stream_position, event_name, event_data, event_data_bytes, NULL::jsonb, created_at";

    public EventLogSql(SchemaObjectNames names)
    {
        var eventLog = names.EventLog;

        ReadStreamForward = $"SELECT {Columns} FROM {eventLog} " +
                            "WHERE stream_id = $1 AND stream_position > $2 ORDER BY stream_position LIMIT $3";

        ReadStreamBackward = $"SELECT {Columns} FROM {eventLog} " +
                             "WHERE stream_id = $1 AND stream_position < $2 ORDER BY stream_position DESC LIMIT $3";

        ReadStreamForwardWithoutMetadata = ReadStreamForward.Replace(Columns, ColumnsWithoutMetadata);
        ReadStreamBackwardWithoutMetadata = ReadStreamBackward.Replace(Columns, ColumnsWithoutMetadata);

        ReadAllForward = $"SELECT {Columns} FROM {eventLog} WHERE position > $1 ORDER BY position LIMIT $2";
        ReadAllBackward = $"SELECT {Columns} FROM {eventLog} WHERE position < $1 ORDER BY position DESC LIMIT $2";
        ReadAllForwardWithoutMetadata = ReadAllForward.Replace(Columns, ColumnsWithoutMetadata);
        ReadAllBackwardWithoutMetadata = ReadAllBackward.Replace(Columns, ColumnsWithoutMetadata);

        StreamExists = $"SELECT EXISTS (SELECT 1 FROM {eventLog} WHERE stream_id = $1)";
        MaxPosition = $"SELECT max(position) FROM {eventLog}";

        ReadCatchUpAll = $"SELECT {Columns} FROM {eventLog} " +
                         "WHERE position > $1 AND position <= $2 ORDER BY position LIMIT $3";

        ReadCatchUpStream = $"SELECT {Columns} FROM {eventLog} " +
                            "WHERE stream_id = $1 AND stream_position > $2 AND position <= $3 " +
                            "ORDER BY stream_position LIMIT $4";
    }

    public string ReadStreamForward { get; }

    public string ReadStreamBackward { get; }

    public string ReadStreamForwardWithoutMetadata { get; }

    public string ReadStreamBackwardWithoutMetadata { get; }

    public string ReadAllForward { get; }

    public string ReadAllBackward { get; }

    public string ReadAllForwardWithoutMetadata { get; }

    public string ReadAllBackwardWithoutMetadata { get; }

    public string StreamExists { get; }

    public string MaxPosition { get; }

    public string ReadCatchUpAll { get; }

    public string ReadCatchUpStream { get; }

    public string ReadStream(ReadDirection direction, bool includeMetadata)
    {
        return (direction, includeMetadata) switch
        {
            (ReadDirection.Forward, true) => ReadStreamForward,
            (ReadDirection.Forward, false) => ReadStreamForwardWithoutMetadata,
            (ReadDirection.Backward, true) => ReadStreamBackward,
            (ReadDirection.Backward, false) => ReadStreamBackwardWithoutMetadata,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    public string ReadAll(ReadDirection direction, bool includeMetadata)
    {
        return (direction, includeMetadata) switch
        {
            (ReadDirection.Forward, true) => ReadAllForward,
            (ReadDirection.Forward, false) => ReadAllForwardWithoutMetadata,
            (ReadDirection.Backward, true) => ReadAllBackward,
            (ReadDirection.Backward, false) => ReadAllBackwardWithoutMetadata,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    /// <summary>
    /// Converts a read origin into the exclusive key bound of the first page. Read origins are inclusive, so the bound
    /// is one step before the origin in the direction of the read.
    /// </summary>
    public static long FirstKeyExclusive<TPos>(ReadDirection direction, ReadOrigin<TPos> origin)
        where TPos : struct, IPosition<TPos>
    {
        var forward = direction == ReadDirection.Forward;

        var resolved = origin.ResolveFor(direction);

        // TPos is a type parameter here, so the position is read with TryGetValue: a pattern cannot bind a variable
        // to a case that mentions one.
        if (resolved.TryGetValue(out ReadOrigin<TPos>.At at))
            return forward ? ToKey(at.Position.Value) - 1 : ToKey(at.Position.Value) + 1;

        // These bounds hold in either direction: reading forward from the end, or backward from the start, is
        // bounded to nothing.
        return resolved is SequenceStart ? -1 : long.MaxValue;

        // Positions beyond long.MaxValue cannot exist; clamping keeps the arithmetic above in range.
        static long ToKey(ulong value) => value >= long.MaxValue - 1 ? long.MaxValue - 1 : (long)value;
    }
}