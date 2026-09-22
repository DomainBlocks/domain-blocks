using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// A document of the event log as it was read, before its event is decoded. A filter is evaluated against it, so that
/// the event of a document that is not selected is never decoded.
/// </summary>
/// <remarks>
/// One instance is set again for each document, so it is only good until the next is read. Whoever wants the event
/// takes it with <see cref="DecodedEvent"/> before then.
/// </remarks>
/// <param name="decoder">Decodes the event of a document when it is asked for.</param>
/// <param name="includeMetadata">
/// Whether events are to have their metadata. A document can have metadata that was only read for a filter to look at.
/// </param>
internal sealed class EventLogDocument<TEvent>(
    IEventDecoder<TEvent, BsonValue, BsonValue> decoder,
    bool includeMetadata) :
    IFilterableEvent
    where TEvent : notnull
{
    private BsonDocument _document = null!;
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> _event;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    public long Position => _document[EventLogEntry.FieldNames.Position].AsInt64;

    public long StreamPosition => _document[EventLogEntry.FieldNames.StreamPosition].AsInt64;

    public string EventName => _document[EventLogEntry.FieldNames.EventName].AsString;

    public string StreamId => _document[EventLogEntry.FieldNames.StreamId].AsString;

    public DateTimeOffset CreatedAt =>
        _document[EventLogEntry.FieldNames.CreatedAtUtc].AsBsonDateTime.ToUniversalTime();

    public object DecodedPayload => DecodedEvent.Payload;

    public void Set(BsonDocument document)
    {
        _document = document;
        _event = default;
        _isDecoded = false;
        _decodeError = null;
    }

    /// <summary>
    /// The event of the document, decoded when it is first asked for, and once, however many ask. If it cannot be
    /// decoded, each who asks is thrown the same exception.
    /// </summary>
    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : Decode();

    // Apart from GetEvent, which every observer of a document calls, so that it stays small enough to inline.
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _event = decoder.Decode(_document, includeMetadata);
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
    /// Looks the key up in the metadata as it is stored, which is what the database goes by too. Metadata that is not
    /// stored as a document has no keys to a filter.
    /// </summary>
    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value)
    {
        value = null;

        if (!_document.TryGetValue(EventLogEntry.FieldNames.Metadata, out var metadata) ||
            metadata is not BsonDocument entries ||
            !entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        // Values are strings. Anything else is given as it is written.
        value = entry.IsString ? entry.AsString : entry.ToString()!;
        return true;
    }
}