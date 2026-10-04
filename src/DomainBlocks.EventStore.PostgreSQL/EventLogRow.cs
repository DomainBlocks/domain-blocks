using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row of the event log as the live feed reads it, before its event is decoded. A subscription evaluates its filter
/// against the row and takes the event only if it selects the row, so an event that no subscription selects is never
/// decoded.
/// </summary>
/// <remarks>
/// A session has one row, which it sets again for each insert, so a row is only valid until the session reads the next.
/// An observer takes what it wants from the row before it returns. The event is decoded when it is first asked for, and
/// once, however many observers ask.
/// </remarks>
internal sealed class EventLogRow<TEvent>(IEventDecoder<TEvent, PostgresEventData, string> decoder) : IFilterableEvent
    where TEvent : notnull
{
    private PostgresEventData _eventData;
    private string? _metadata;
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> _event;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    public long Position { get; private set; }

    public string StreamId { get; private set; } = string.Empty;

    public long StreamPosition { get; private set; }

    public string EventName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The event of the row. If it cannot be decoded, everyone who asks is thrown the same exception.
    /// </summary>
    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : Decode();

    /// <summary>
    /// Sets the row to the next insert. The metadata is as it is stored, a JSON object of strings, or
    /// <see langword="null"/> if the event has none.
    /// </summary>
    public void Set(
        long position,
        string streamId,
        long streamPosition,
        string eventName,
        PostgresEventData eventData,
        string? metadata,
        DateTimeOffset createdAt)
    {
        Position = position;
        StreamId = streamId;
        StreamPosition = streamPosition;
        EventName = eventName;
        CreatedAt = createdAt;
        _eventData = eventData;
        _metadata = metadata;
        _event = default;
        _isDecoded = false;
        _decodeError = null;
    }

    /// <summary>
    /// Looks the key up in the metadata as it is stored, which is what the database goes by when it evaluates the same
    /// filter. Nothing is kept of the metadata, as a filter asks for a key or two of an event, and most events are
    /// asked nothing.
    /// </summary>
    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value)
    {
        value = null;

        if (_metadata is null)
            return false;

        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(_metadata.Length));

        try
        {
            var length = Encoding.UTF8.GetBytes(_metadata, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, length));

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isKey = reader.ValueTextEquals(key);
                reader.Read();

                if (!isKey)
                {
                    reader.Skip();
                    continue;
                }

                // Values are strings. Anything else is given as it is written, which is what ->> gives the database.
                if (reader.TokenType == JsonTokenType.String)
                {
                    value = reader.GetString()!;
                }
                else
                {
                    var start = (int)reader.TokenStartIndex;
                    reader.Skip();
                    value = Encoding.UTF8.GetString(buffer.AsSpan(start, (int)reader.BytesConsumed - start));
                }

                return true;
            }

            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Kept apart from DecodedEvent, which every observer that selects the row calls, so that it stays small enough to
    // inline.
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _event = decoder.Decode(Position, StreamId, StreamPosition, EventName, _eventData, _metadata, CreatedAt);
        }
        catch (Exception ex)
        {
            _decodeError = ExceptionDispatchInfo.Capture(ex);
            throw;
        }

        _isDecoded = true;

        return _event;
    }
}