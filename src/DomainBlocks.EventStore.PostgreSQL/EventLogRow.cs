using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row from the live feed whose event is decoded only when an observer asks for it, so an event that no subscription
/// selects is never decoded.
/// </summary>
/// <remarks>
/// A session reuses one row for every insert, so a row is valid only until the next is read. The event is decoded once,
/// on first request.
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
    /// The decoded event. A decode failure is rethrown to every caller.
    /// </summary>
    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : Decode();

    /// <summary>
    /// Sets the row to the next insert. <paramref name="metadata"/> is the stored JSON, or <see langword="null"/>.
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
    /// Reads the key from the stored JSON, as the database does when it evaluates the same filter.
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

                // A value that is not a string is returned as written, which matches what ->> returns in the database.
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

    // Out of DecodedEvent so the getter, read by every observer that selects the row, stays small enough to inline.
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