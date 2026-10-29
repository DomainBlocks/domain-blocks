using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// A document from the change stream whose event is decoded only when an observer asks for it, so an event that no
/// subscription selects is never decoded.
/// </summary>
/// <remarks>
/// A feed connection reuses one document for every change, so a document is valid only until the next change is
/// delivered. The payload and metadata are decoded together, once, on the first request for either. The context is read
/// once, on first request.
/// </remarks>
internal sealed class EventLogDocument<TEvent>(IEventDecoder<TEvent, BsonValue, BsonValue> decoder) :
    IFilterableEvent,
    IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private BsonDocument _document = [];
    private DecodedEvent<TEvent> _decoded;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;
    private ReadEventContext<string, StreamPosition, LogPosition> _context;
    private bool _hasContext;

    public string EventName => _document[EventLogEntry.FieldNames.EventName].AsString;

    public string StreamId => _document[EventLogEntry.FieldNames.StreamId].AsString;

    public DateTimeOffset CreatedAt =>
        _document[EventLogEntry.FieldNames.CreatedAtUtc].AsBsonDateTime.ToUniversalTime();

    /// <summary>
    /// The decoded payload. A decode failure is rethrown to every caller.
    /// </summary>
    public TEvent Payload => Decoded.Payload;

    /// <summary>
    /// The decoded metadata. A decode failure is rethrown to every caller.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata => Decoded.Metadata;

    /// <summary>
    /// The event's context, which is read from the document without decoding the event.
    /// </summary>
    public ReadEventContext<string, StreamPosition, LogPosition> Context => _hasContext ? _context : ReadContext();

    private DecodedEvent<TEvent> Decoded => _isDecoded ? _decoded : Decode();

    /// <summary>
    /// Sets the document to the next change.
    /// </summary>
    public void Set(BsonDocument document)
    {
        _document = document;
        _decoded = default;
        _isDecoded = false;
        _decodeError = null;
        _context = default;
        _hasContext = false;
    }

    /// <summary>
    /// Reads the key from the stored metadata document. Metadata that is not stored as a document has no keys for a
    /// filter to find.
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

        // The store writes metadata values as strings. Any other value is returned as written.
        value = entry.IsString ? entry.AsString : entry.ToString()!;

        return true;
    }

    // Out of Decoded so the getter, read by every observer that selects the document, stays small enough to inline.
    private DecodedEvent<TEvent> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _decoded = decoder.Decode(
                EventName,
                _document[EventLogEntry.FieldNames.EventData],
                _document.GetValue(EventLogEntry.FieldNames.Metadata, BsonNull.Value));
        }
        catch (Exception ex)
        {
            _decodeError = ExceptionDispatchInfo.Capture(ex);
            throw;
        }

        _isDecoded = true;

        return _decoded;
    }

    private ReadEventContext<string, StreamPosition, LogPosition> ReadContext()
    {
        _context = ReadEventContext.Create(
            StreamId,
            EventName,
            CreatedAt,
            StreamPosition.FromInt64(_document[EventLogEntry.FieldNames.StreamPosition].AsInt64),
            LogPosition.FromInt64(_document[EventLogEntry.FieldNames.Position].AsInt64));

        _hasContext = true;

        return _context;
    }
}