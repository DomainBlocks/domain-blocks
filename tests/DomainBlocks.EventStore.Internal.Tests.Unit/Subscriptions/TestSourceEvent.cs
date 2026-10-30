using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Subscriptions;

/// <summary>
/// Stands in for a store's undecoded live event. As in the stores, one instance is reused for every item, and it
/// decodes once per item, on the first request for its payload or metadata.
/// </summary>
internal sealed class TestSourceEvent : IFilterableEvent, IReadEvent<string, string, StreamPosition, LogPosition>
{
    private long _position;
    private bool _isDecoded;

    public int DecodeCount { get; private set; }

    public Exception? DecodeFailure { get; set; }

    public string EventName => "OrderPlaced";

    public string StreamId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public string Payload
    {
        get
        {
            Decode();
            return "payload";
        }
    }

    public IReadOnlyDictionary<string, string> Metadata
    {
        get
        {
            Decode();
            return new Dictionary<string, string>();
        }
    }

    public ReadEventContext<string, StreamPosition, LogPosition> Context =>
        ReadEventContext.Create(
            StreamId,
            EventName,
            CreatedAt,
            StreamPosition.FromInt64(_position),
            LogPosition.FromInt64(_position));

    public void Set(long position, string streamId)
    {
        _position = position;
        StreamId = streamId;
        _isDecoded = false;
    }

    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value)
    {
        value = null;
        return false;
    }

    private void Decode()
    {
        if (!_isDecoded)
        {
            _isDecoded = true;
            DecodeCount++;
        }

        if (DecodeFailure is { } failure)
            throw failure;
    }
}