using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// A document of the event log as the change stream delivers it, before its event is decoded. A subscription evaluates
/// its filter against the document and takes the event only if it selects the document, so an event that no
/// subscription selects is never decoded.
/// </summary>
/// <remarks>
/// The store has one document, which it sets again for each change, so a document is only valid until the next change
/// is delivered. An observer takes what it wants from the document before it returns. The event is decoded when it is
/// first asked for, and once, however many observers ask.
/// </remarks>
internal sealed class EventLogDocument<TEvent>(IEventDecoder<TEvent, BsonValue, BsonValue> decoder) : IFilterableEvent
    where TEvent : notnull
{
    private BsonDocument _document = [];
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> _event;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    public string EventName => _document[EventLogEntry.FieldNames.EventName].AsString;

    public string StreamId => _document[EventLogEntry.FieldNames.StreamId].AsString;

    public DateTimeOffset CreatedAt =>
        _document[EventLogEntry.FieldNames.CreatedAtUtc].AsBsonDateTime.ToUniversalTime();

    /// <summary>
    /// The event of the document. If it cannot be decoded, everyone who asks is thrown the same exception.
    /// </summary>
    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : Decode();

    /// <summary>
    /// Sets the document to the next change.
    /// </summary>
    public void Set(BsonDocument document)
    {
        _document = document;
        _event = default;
        _isDecoded = false;
        _decodeError = null;
    }

    /// <summary>
    /// Looks the key up in the metadata as it is stored, which is what the database goes by when it evaluates the same
    /// filter. Metadata that is not stored as a document has no keys for a filter to find.
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

    // Kept apart from DecodedEvent, which every observer that selects the document calls, so that it stays small
    // enough to inline.
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _event = decoder.Decode(_document);
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