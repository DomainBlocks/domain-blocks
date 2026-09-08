using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStore
{
    /// <summary>
    /// Creates an event store over an existing data source. The caller owns the data source's lifetime; the store only
    /// borrows connections from it. The schema must have been initialized with
    /// <see cref="PostgresEventStoreAdmin.EnsureInitializedAsync"/>.
    /// </summary>
    public static PostgresEventStore<TEvent> Create<TEvent>(
        NpgsqlDataSource dataSource,
        EventCodec<TEvent, PostgresEventData, string> eventCodec,
        PostgresEventStoreOptions? options = null,
        ILogger? logger = null)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(eventCodec);

        options ??= new PostgresEventStoreOptions();

        var names = new SqlNames(options.Schema);
        var appender = new BatchingAppender(dataSource, names, options, logger);
        var reader = new EventLogReader(dataSource, new EventLogSql(names), options.ReadBatchSize);

        return new PostgresEventStore<TEvent>(appender, reader, eventCodec);
    }
}

public sealed class PostgresEventStore<TEvent> : IPostgresEventStore<TEvent> where TEvent : notnull
{
    private readonly IAppender _appender;
    private readonly EventLogReader _reader;
    private readonly EventCodec<TEvent, PostgresEventData, string> _eventCodec;

    internal PostgresEventStore(
        IAppender appender,
        EventLogReader reader,
        EventCodec<TEvent, PostgresEventData, string> eventCodec)
    {
        _appender = appender;
        _reader = reader;
        _eventCodec = eventCodec;
    }

    public async Task AppendAsync(
        string streamId,
        IEnumerable<AppendableEvent<TEvent>> events,
        ExpectedStreamState<StreamPosition>? expectedState = null,
        Guid? commitId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);
        ArgumentNullException.ThrowIfNull(events);
        ThrowIfContainsNul(streamId, nameof(streamId));

        expectedState ??= ExpectedStreamState.Any<StreamPosition>();
        commitId ??= Guid.NewGuid();
        options ??= AppendOptions.Default;

        var encodedEvents = _eventCodec.Encoder.Encode(events).ToArray();

        if (encodedEvents.Length == 0)
            return;

        foreach (var (_, eventData, metadata) in encodedEvents)
        {
            if (eventData.IsJson)
                ThrowIfContainsNul(eventData.Json, nameof(events));

            if (metadata is not null)
                ThrowIfContainsNul(metadata, nameof(events));
        }

        var request = new AppendRequest(streamId, expectedState.Value, commitId.Value, encodedEvents);

        await _appender.AppendAsync(request, options.Timeout, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<LogPosition>? origin = null,
        ReadAllOptions? options = null)
    {
        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward
                ? ReadOrigin.Start<LogPosition>()
                : ReadOrigin.End<LogPosition>();

            options ??= ReadAllOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
                yield break;

            var firstKeyExclusive = EventLogSql.FirstKeyExclusive(direction, origin);

            var rows = _reader.ReadAllAsync(
                direction,
                firstKeyExclusive,
                options.MaxCount,
                options.IncludeMetadata,
                cancellationToken);

            await foreach (var row in rows.ConfigureAwait(false))
                yield return _eventCodec.Decoder.Decode(row);
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDirection direction = ReadDirection.Forward,
        ReadOrigin<StreamPosition>? origin = null,
        ReadStreamOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(streamId);

        return Impl();

        async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Impl(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            origin ??= direction == ReadDirection.Forward
                ? ReadOrigin.Start<StreamPosition>()
                : ReadOrigin.End<StreamPosition>();

            options ??= ReadStreamOptions.Default;

            if (direction.ProducesEmptyReadFrom(origin))
            {
                await ThrowIfStreamNotFoundAsync(streamId, options, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            var firstKeyExclusive = EventLogSql.FirstKeyExclusive(direction, origin);
            var isEmpty = true;

            var rows = _reader.ReadStreamAsync(
                streamId,
                direction,
                firstKeyExclusive,
                options.MaxCount,
                options.IncludeMetadata,
                cancellationToken);

            await foreach (var row in rows.ConfigureAwait(false))
            {
                isEmpty = false;
                yield return _eventCodec.Decoder.Decode(row);
            }

            // Unlike an empty edge-case read, an empty result here may simply mean the origin is beyond the end of an
            // existing stream, so the stream's existence is checked rather than assumed.
            if (isEmpty)
                await ThrowIfStreamNotFoundAsync(streamId, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionOrigin<LogPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionOrigin<StreamPosition>? origin = null,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask DisposeAsync() => _appender.DisposeAsync();

    private async Task ThrowIfStreamNotFoundAsync(
        string streamId,
        ReadStreamOptions options,
        CancellationToken cancellationToken)
    {
        if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
            !await _reader.StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
        {
            throw new StreamNotFoundException(streamId);
        }
    }

    /// <summary>
    /// PostgreSQL text and jsonb values cannot contain NUL. Rejecting it here, per caller, keeps one bad payload from
    /// faulting a whole batch of unrelated appends.
    /// </summary>
    private static void ThrowIfContainsNul(string value, string paramName)
    {
        if (value.Contains('\0') || value.Contains("\\u0000", StringComparison.Ordinal))
            throw new ArgumentException("PostgreSQL text values cannot contain NUL characters.", paramName);
    }
}
