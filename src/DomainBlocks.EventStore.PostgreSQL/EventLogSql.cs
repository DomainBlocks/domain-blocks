using DomainBlocks.EventStore.Abstractions;

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

    public EventLogSql(SqlNames names)
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
        return (direction, origin) switch
        {
            (ReadDirection.Forward, ReadOrigin<TPos>.Start) => -1,
            (ReadDirection.Forward, ReadOrigin<TPos>.At at) => ToKey(at.Position.Value) - 1,
            (ReadDirection.Backward, ReadOrigin<TPos>.End) => long.MaxValue,
            (ReadDirection.Backward, ReadOrigin<TPos>.At at) => ToKey(at.Position.Value) + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unsupported direction and origin.")
        };

        // Positions beyond long.MaxValue cannot exist; clamping keeps the arithmetic above in range.
        static long ToKey(ulong value) => value >= long.MaxValue - 1 ? long.MaxValue - 1 : (long)value;
    }
}
