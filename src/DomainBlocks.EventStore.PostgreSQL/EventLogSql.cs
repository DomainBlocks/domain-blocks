namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The read queries over the event log, paged by keyset. Each page selects the rows after the last position of the
/// previous page. Positions never change and are assigned in commit order, so no row is skipped or repeated between
/// pages.
/// </summary>
internal sealed class EventLogSql
{
    private const string Columns =
        "position, stream_id, stream_position, event_name, event_data, event_data_bytes, metadata, created_at";

    private const string ColumnsWithoutMetadata =
        "position, stream_id, stream_position, event_name, event_data, event_data_bytes, NULL::jsonb, created_at";

    /// <summary>
    /// Parameters bound by a <see cref="ReadStream"/> page: stream ID, key, and limit. Condition parameters follow.
    /// </summary>
    public const int ReadStreamParameterCount = 3;

    /// <summary>
    /// Parameters bound by a <see cref="ReadAll"/> page: key and limit. Condition parameters follow.
    /// </summary>
    public const int ReadAllParameterCount = 2;

    /// <summary>
    /// Parameters bound by a <see cref="CatchUpAll"/> page: key, high-water mark, and limit. Condition parameters
    /// follow.
    /// </summary>
    public const int CatchUpAllParameterCount = 3;

    /// <summary>
    /// Parameters bound by a <see cref="CatchUpStream"/> page: stream ID, key, high-water mark, and limit. Condition
    /// parameters follow.
    /// </summary>
    public const int CatchUpStreamParameterCount = 4;

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
        MaxStreamPosition = $"SELECT max(stream_position) FROM {eventLog} WHERE stream_id = $1";

        ReadCatchUpAll = $"SELECT {Columns} FROM {eventLog} " +
                         "WHERE position > $1 AND position <= $2 ORDER BY position LIMIT $3";

        ReadCatchUpStream = $"SELECT {Columns} FROM {eventLog} " +
                            "WHERE stream_id = $1 AND stream_position > $2 AND stream_position <= $3 " +
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

    public string MaxStreamPosition { get; }

    public string ReadCatchUpAll { get; }

    public string ReadCatchUpStream { get; }

    public string CatchUpAll(string? condition = null) =>
        condition is null ? ReadCatchUpAll : WithCondition(ReadCatchUpAll, condition);

    public string CatchUpStream(string? condition = null) =>
        condition is null ? ReadCatchUpStream : WithCondition(ReadCatchUpStream, condition);

    public string ReadStream(ReadDirection direction, bool includeMetadata, string? condition = null)
    {
        var query = (direction, includeMetadata) switch
        {
            (ReadDirection.Forward, true) => ReadStreamForward,
            (ReadDirection.Forward, false) => ReadStreamForwardWithoutMetadata,
            (ReadDirection.Backward, true) => ReadStreamBackward,
            (ReadDirection.Backward, false) => ReadStreamBackwardWithoutMetadata,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

        return condition is null ? query : WithCondition(query, condition);
    }

    public string ReadAll(ReadDirection direction, bool includeMetadata, string? condition = null)
    {
        var query = (direction, includeMetadata) switch
        {
            (ReadDirection.Forward, true) => ReadAllForward,
            (ReadDirection.Forward, false) => ReadAllForwardWithoutMetadata,
            (ReadDirection.Backward, true) => ReadAllBackward,
            (ReadDirection.Backward, false) => ReadAllBackwardWithoutMetadata,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };

        return condition is null ? query : WithCondition(query, condition);
    }

    /// <summary>
    /// Read origins are inclusive, so the first page's exclusive bound is one step before the origin.
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

        // Positions beyond long.MaxValue cannot exist, and clamping keeps the arithmetic above in range.
        static long ToKey(ulong value) => value >= long.MaxValue - 1 ? long.MaxValue - 1 : (long)value;
    }

    // Every read query has a WHERE clause followed by one ORDER BY, so the condition goes between them. The limit then
    // counts only the rows that match the condition.
    private static string WithCondition(string query, string condition)
    {
        const string orderBy = " ORDER BY ";
        var index = query.LastIndexOf(orderBy, StringComparison.Ordinal);

        return $"{query[..index]} AND {condition}{query[index..]}";
    }
}