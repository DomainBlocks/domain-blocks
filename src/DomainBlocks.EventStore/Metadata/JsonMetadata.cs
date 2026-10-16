using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace DomainBlocks.EventStore.Metadata;

/// <summary>
/// A read-only view of the top-level properties of a JSON object, read in place from its text.
/// Lookups follow the database's <c>-&gt;&gt;</c>: a string value is unescaped, any other value is returned as written,
/// a JSON null is treated as absent, and a duplicated key resolves to its last occurrence.
/// </summary>
/// <remarks>
/// The instance is reusable: <see cref="Reset"/> points it at the next document, and the UTF-8 buffer it transcodes
/// into is kept between documents, so steady-state lookups allocate nothing except the string a hit returns.
/// <see cref="ValueEquals"/> allocates nothing at all. Enumeration, <see cref="Count"/>, <see cref="Keys"/> and
/// <see cref="Values"/> materialise strings and are not on the hot path. Not thread-safe.
/// </remarks>
internal sealed class JsonMetadata : IReadOnlyDictionary<string, string>, IDisposable
{
    private string? _json;
    private byte[] _utf8 = [];

    // -1 until the first lookup, so a document nobody queries is never transcoded.
    private int _length = -1;

    /// <summary>
    /// Points the view at <paramref name="json"/>, a JSON object or <see langword="null"/> for none.
    /// </summary>
    public void Reset(string? json)
    {
        _json = json;
        _length = json is null ? 0 : -1;
    }

    /// <summary>
    /// Points the view at a UTF-8 document of <paramref name="length"/> bytes, which the caller writes into the
    /// returned buffer before the next lookup. The string form is only built if <see cref="Json"/> is read.
    /// </summary>
    public Memory<byte> ResetUtf8(int length)
    {
        EnsureCapacity(length);
        _json = null;
        _length = length;

        return _utf8.AsMemory(0, length);
    }

    /// <summary>
    /// The document as a string, built from the UTF-8 form on first read when it was set with <see cref="ResetUtf8"/>.
    /// </summary>
    public string? Json => _json ??= _length > 0 ? Encoding.UTF8.GetString(_utf8, 0, _length) : null;

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        var json = Utf8;

        if (!TryFind(json, key, out var reader))
        {
            value = null;
            return false;
        }

        value = ReadValue(ref reader, json);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="key"/> holds <paramref name="expected"/>, compared as <see cref="TryGetValue"/> would
    /// return it, without allocating.
    /// </summary>
    public bool ValueEquals(string key, string expected)
    {
        var json = Utf8;

        if (!TryFind(json, key, out var reader))
            return false;

        if (reader.TokenType == JsonTokenType.String)
            return reader.ValueTextEquals(expected);

        var start = (int)reader.TokenStartIndex;
        reader.Skip();

        return Utf8Equals(json[start..(int)reader.BytesConsumed], expected);
    }

    public bool ContainsKey(string key) => TryFind(Utf8, key, out _);

    public string this[string key] =>
        TryGetValue(key, out var value) ? value : throw new KeyNotFoundException($"The key '{key}' was not found.");

    public int Count
    {
        get
        {
            var count = 0;

            foreach (var _ in this)
                count++;

            return count;
        }
    }

    public IEnumerable<string> Keys => this.Select(x => x.Key);

    public IEnumerable<string> Values => this.Select(x => x.Value);

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (_utf8.Length > 0)
            ArrayPool<byte>.Shared.Return(_utf8);

        _utf8 = [];
        _json = null;
        _length = -1;
    }

    private ReadOnlySpan<byte> Utf8
    {
        get
        {
            if (_length < 0)
                Transcode();

            return _utf8.AsSpan(0, _length);
        }
    }

    private void Transcode()
    {
        if (_json is null)
        {
            _length = 0;
            return;
        }

        EnsureCapacity(Encoding.UTF8.GetMaxByteCount(_json.Length));
        _length = Encoding.UTF8.GetBytes(_json, _utf8);
    }

    private void EnsureCapacity(int required)
    {
        if (_utf8.Length >= required)
            return;

        if (_utf8.Length > 0)
            ArrayPool<byte>.Shared.Return(_utf8);

        _utf8 = ArrayPool<byte>.Shared.Rent(required);
    }

    // Leaves the reader on the value of the last occurrence of the key.
    private static bool TryFind(ReadOnlySpan<byte> json, string key, out Utf8JsonReader value)
    {
        value = default;

        var reader = new Utf8JsonReader(json);
        var found = false;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isKey = reader.ValueTextEquals(key);
            reader.Read();

            if (isKey)
            {
                value = reader;
                found = true;
            }

            reader.Skip();
        }

        return found && value.TokenType != JsonTokenType.Null;
    }

    // A value that is not a string is returned as written, which matches what ->> returns in the database.
    private static string ReadValue(ref Utf8JsonReader reader, ReadOnlySpan<byte> json)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString()!;

        var start = (int)reader.TokenStartIndex;
        reader.Skip();

        return Encoding.UTF8.GetString(json[start..(int)reader.BytesConsumed]);
    }

    private static bool Utf8Equals(ReadOnlySpan<byte> utf8, string text)
    {
        // UTF-8 never needs fewer bytes than UTF-16 needs chars.
        if (text.Length > utf8.Length)
            return false;

        char[]? rented = null;
        var chars = utf8.Length <= 256 ? stackalloc char[256] : rented = ArrayPool<char>.Shared.Rent(utf8.Length);

        try
        {
            var length = Encoding.UTF8.GetChars(utf8, chars);
            return chars[..length].SequenceEqual(text);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    // A later occurrence of the key wins, so this one is not part of the dictionary.
    private static bool IsShadowed(Utf8JsonReader reader, string key)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(key))
                return true;

            reader.Read();
            reader.Skip();
        }

        return false;
    }

    /// <summary>
    /// Resumes a reader over the remaining text on each step, since a reader cannot be held between calls.
    /// </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, string>>
    {
        private readonly JsonMetadata _owner;
        private int _offset;
        private JsonReaderState _state;
        private bool _started;

        internal Enumerator(JsonMetadata owner)
        {
            _owner = owner;
            Current = default;
        }

        public KeyValuePair<string, string> Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var json = _owner.Utf8;

            if (_offset >= json.Length)
                return false;

            var remaining = json[_offset..];
            var reader = new Utf8JsonReader(remaining, isFinalBlock: true, _state);

            if (!_started)
            {
                _started = true;

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    _offset = json.Length;
                    return false;
                }
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var key = reader.GetString()!;
                reader.Read();

                var afterValue = reader;
                afterValue.Skip();

                if (reader.TokenType == JsonTokenType.Null || IsShadowed(afterValue, key))
                {
                    reader = afterValue;
                    continue;
                }

                Current = KeyValuePair.Create(key, ReadValue(ref reader, remaining));
                _offset += (int)reader.BytesConsumed;
                _state = reader.CurrentState;

                return true;
            }

            _offset = json.Length;
            return false;
        }

        public void Reset()
        {
            _offset = 0;
            _state = default;
            _started = false;
            Current = default;
        }

        public readonly void Dispose()
        {
        }
    }
}