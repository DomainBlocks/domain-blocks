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
/// The store reuses one document for every change, so a document is valid only until the next change is delivered. The
/// event is decoded once, on first request.
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
    /// The decoded event. A decode failure is rethrown to every caller.
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

    // Out of DecodedEvent so the getter, read by every observer that selects the document, stays small enough to
    // inline.
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