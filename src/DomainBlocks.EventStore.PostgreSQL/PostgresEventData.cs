using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The stored representation of an event's data: either a JSON document, held in a <c>jsonb</c> column, or raw bytes,
/// held in a <c>bytea</c> column.
/// </summary>
/// <remarks>
/// A union of <see cref="string"/> (JSON) and <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/>: either converts
/// implicitly to this type, and a <see langword="switch"/> over both is exhaustive. The default value holds neither,
/// and matches <see langword="null"/>.
/// </remarks>
[Union]
public readonly struct PostgresEventData : IUnion
{
    private readonly string? _json;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly bool _isBytes;

    public PostgresEventData(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        _json = json;
    }

    public PostgresEventData(ReadOnlyMemory<byte> bytes)
    {
        _bytes = bytes;
        _isBytes = true;
    }

    public static PostgresEventData FromJson(string json)
    {
        return new PostgresEventData(json);
    }

    public static PostgresEventData FromBytes(ReadOnlyMemory<byte> bytes)
    {
        return new PostgresEventData(bytes);
    }

    public string Json => _json ?? throw new InvalidOperationException("Event data is not JSON.");

    public ReadOnlyMemory<byte> Bytes =>
        _isBytes ? _bytes : throw new InvalidOperationException("Event data is not bytes.");

    /// <summary>
    /// Whether this holds JSON or bytes; <see langword="false"/> only for the default value.
    /// </summary>
    public bool HasValue => _json is not null || _isBytes;

    /// <summary>
    /// The JSON or the bytes, or <see langword="null"/> for the default value. Reading bytes through this property
    /// boxes them; prefer <see cref="Bytes"/> or <see cref="TryGetValue(out ReadOnlyMemory{byte})"/>.
    /// </summary>
    public object? Value
    {
        get
        {
            if (_json is not null)
                return _json;

            // Not `_isBytes ? _bytes : null`: that null would convert to an empty ReadOnlyMemory<byte>, via byte[].
            return _isBytes ? _bytes : (object?)null;
        }
    }

    public bool TryGetValue([MaybeNullWhen(false)] out string json)
    {
        json = _json;
        return _json is not null;
    }

    public bool TryGetValue(out ReadOnlyMemory<byte> bytes)
    {
        bytes = _bytes;
        return _isBytes;
    }

    public override string ToString()
    {
        return this switch
        {
            string json => json,
            ReadOnlyMemory<byte> bytes => $"{bytes.Length} bytes",
            null => "(none)"
        };
    }
}