using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A row read for subscriptions, whose event is decoded only when a subscription asks for it, so an event that no
/// subscription selects is never decoded.
/// </summary>
/// <remarks>
/// <para>
/// A source reuses one row for every row it reads, so a row is valid only until the next is read. The payload and
/// metadata are decoded together, once, on the first request for either.
/// </para>
/// <para>
/// Every subscription's filter evaluates the same row, so a metadata lookup is answered once per row and key: the
/// stored JSON is transcoded to UTF-8 once, each key is found once, and later lookups of the key reuse the result.
/// The values are copied into a buffer of chars that the row reuses, so no lookup allocates.
/// </para>
/// </remarks>
internal sealed class EventLogRow<TEvent>(IEventDecoder<TEvent, PostgresEventData, string> decoder) :
    IFilterableEvent,
    IReadEvent<TEvent, string, StreamPosition, LogPosition>
    where TEvent : notnull
{
    private PostgresEventData _eventData;
    private string? _metadata;
    private DecodedEvent<TEvent> _decoded;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    // The stored metadata as UTF-8, transcoded on the first lookup. A length of -1 means not yet transcoded.
    private byte[] _utf8 = [];
    private int _utf8Length;

    // The values found so far, in a buffer that every row reuses, and the result of each key looked up.
    private char[] _chars = [];
    private int _charLength;
    private MetadataLookup[] _lookups = [];
    private int _lookupCount;

    public long Position { get; private set; }

    public string StreamId { get; private set; } = string.Empty;

    public long StreamPosition { get; private set; }

    public string EventName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The decoded payload. A decode failure is rethrown to every caller.
    /// </summary>
    public TEvent Payload => Decoded.Payload;

    /// <summary>
    /// The decoded metadata. A decode failure is rethrown to every caller.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata => Decoded.Metadata;

    /// <summary>
    /// The event's context, which is read from the row without decoding the event.
    /// </summary>
    public ReadEventContext<string, StreamPosition, LogPosition> Context =>
        ReadEventContext.Create(
            StreamId,
            EventName,
            CreatedAt,
            EventStore.StreamPosition.FromInt64(StreamPosition),
            LogPosition.FromInt64(Position));

    private DecodedEvent<TEvent> Decoded => _isDecoded ? _decoded : Decode();

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
        _decoded = default;
        _isDecoded = false;
        _decodeError = null;
        _utf8Length = metadata is null ? 0 : -1;
        _charLength = 0;
        _lookupCount = 0;
    }

    /// <summary>
    /// Reads the key from the stored JSON, as the database does when it evaluates the same filter. The value is valid
    /// until the row is set to the next.
    /// </summary>
    public bool TryGetMetadata(string key, out ReadOnlySpan<char> value)
    {
        for (var i = 0; i < _lookupCount; i++)
        {
            ref var lookup = ref _lookups[i];

            if (ReferenceEquals(lookup.Key, key) || string.Equals(lookup.Key, key, StringComparison.Ordinal))
                return lookup.TryGetValue(_chars, out value);
        }

        if (_lookupCount == _lookups.Length)
            Array.Resize(ref _lookups, Math.Max(4, _lookups.Length * 2));

        ref var added = ref _lookups[_lookupCount++];
        added = Find(key);

        return added.TryGetValue(_chars, out value);
    }

    private ReadOnlySpan<byte> Utf8
    {
        get
        {
            if (_utf8Length < 0)
            {
                var maxLength = Encoding.UTF8.GetMaxByteCount(_metadata!.Length);

                if (_utf8.Length < maxLength)
                    _utf8 = new byte[maxLength];

                _utf8Length = Encoding.UTF8.GetBytes(_metadata, _utf8);
            }

            return _utf8.AsSpan(0, _utf8Length);
        }
    }

    // Finds the key in the stored JSON and copies its value into the char buffer.
    private MetadataLookup Find(string key)
    {
        var json = Utf8;

        if (json.IsEmpty)
            return MetadataLookup.NotFound(key);

        var reader = new Utf8JsonReader(json);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return MetadataLookup.NotFound(key);

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isKey = reader.ValueTextEquals(key);
            reader.Read();

            if (!isKey)
            {
                reader.Skip();
                continue;
            }

            var start = _charLength;
            int length;

            // A string is unescaped. A value that is not a string is returned as written, which matches what ->>
            // returns in the database. Either way, the value has no more chars than its JSON has bytes.
            if (reader.TokenType == JsonTokenType.String)
            {
                EnsureChars(reader.ValueSpan.Length);
                length = reader.CopyString(_chars.AsSpan(start));
            }
            else
            {
                var tokenStart = (int)reader.TokenStartIndex;
                reader.Skip();
                var written = json[tokenStart..(int)reader.BytesConsumed];

                EnsureChars(written.Length);
                length = Encoding.UTF8.GetChars(written, _chars.AsSpan(start));
            }

            _charLength += length;

            return new MetadataLookup(key, start, length);
        }

        return MetadataLookup.NotFound(key);
    }

    // A larger buffer leaves the earlier values as they were, because a lookup refers to its value by offset.
    private void EnsureChars(int count)
    {
        if (_chars.Length - _charLength < count)
            Array.Resize(ref _chars, Math.Max(_chars.Length * 2, _charLength + count));
    }

    // Out of Decoded so the getter, read by every observer that selects the row, stays small enough to inline.
    private DecodedEvent<TEvent> Decode()
    {
        _decodeError?.Throw();

        try
        {
            _decoded = decoder.Decode(EventName, _eventData, _metadata);
        }
        catch (Exception ex)
        {
            _decodeError = ExceptionDispatchInfo.Capture(ex);
            throw;
        }

        _isDecoded = true;

        return _decoded;
    }

    // The result of looking up one key in the current row. A start of -1 means the row has no value for the key.
    private readonly record struct MetadataLookup(string Key, int Start, int Length)
    {
        public static MetadataLookup NotFound(string key) => new(key, -1, 0);

        public bool TryGetValue(char[] chars, out ReadOnlySpan<char> value)
        {
            value = Start < 0 ? default : chars.AsSpan(Start, Length);
            return Start >= 0;
        }
    }
}