using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row of the event log as it was read, before its event is decoded. A filter is evaluated against it, so that the
/// event of a row that is not selected is never decoded.
/// </summary>
/// <remarks>
/// One instance is set again for each row, so it is only good until the next row is read. Whoever wants the event
/// takes it with <see cref="DecodedEvent"/> before then.
/// </remarks>
/// <param name="decoder">Decodes the event of a row when it is asked for.</param>
/// <param name="includeMetadata">
/// Whether events are to have their metadata. A row can have metadata that was only read for a filter to look at.
/// </param>
internal sealed class EventLogRow<TEvent>(
    IEventDecoder<TEvent, PostgresEventData, string> decoder,
    bool includeMetadata) :
    IFilterableEvent
    where TEvent : notnull
{
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> _event;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    public long Position { get; private set; }

    public string StreamId { get; private set; } = null!;

    public long StreamPosition { get; private set; }

    public string EventName { get; private set; } = null!;

    public PostgresEventData EventData { get; private set; }

    /// <summary>
    /// The metadata as it is stored, a JSON object of strings, or <see langword="null"/> if the event has none.
    /// </summary>
    public string? Metadata { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public object DecodedPayload => DecodedEvent.Payload;

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
        EventData = eventData;
        Metadata = metadata;
        CreatedAt = createdAt;
        _event = default;
        _isDecoded = false;
        _decodeError = null;
    }

    /// <summary>
    /// The event of the row, decoded when it is first asked for, and once, however many ask. If it cannot be decoded,
    /// each who asks is thrown the same exception.
    /// </summary>
    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : Decode();

    // Apart from GetEvent, which every observer of a row calls, so that it stays small enough to inline.
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _event = decoder.Decode(
                Position,
                StreamId,
                StreamPosition,
                EventName,
                EventData,
                includeMetadata ? Metadata : null,
                CreatedAt);
        }
        catch (Exception ex)
        {
            _decodeError = ExceptionDispatchInfo.Capture(ex);
            throw;
        }

        _isDecoded = true;
        return _event;
    }

    /// <summary>
    /// Looks the key up in the metadata as it is stored, which is what the database goes by too. Nothing is kept of
    /// the metadata, as a filter asks for a key or two of an event, and most events are asked nothing.
    /// </summary>
    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value)
    {
        value = null;

        if (Metadata is null)
            return false;

        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(Metadata.Length));

        try
        {
            var length = Encoding.UTF8.GetBytes(Metadata, buffer);
            var reader = new Utf8JsonReader(buffer.AsSpan(0, length));

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isKey = reader.ValueTextEquals(key);
                reader.Read();

                if (isKey)
                {
                    // Values are strings. Anything else is given as it is written.
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

                reader.Skip();
            }

            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}