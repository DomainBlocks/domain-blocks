using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.Filtering;

namespace DomainBlocks.EventStore.Tests.Unit.Subscriptions;

/// <summary>
/// An event as a store holds it, whose payload is a string that <see cref="TestDecoder"/> decodes on request.
/// </summary>
internal sealed class TestSourceEvent(
    long logPosition,
    string streamId,
    long streamPosition,
    string payload,
    TestDecoder decoder) :
    IFilterableEvent,
    IReadEvent<string, string, StreamPosition, LogPosition>
{
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public string EventName => "TestEvent";

    public string StreamId => streamId;

    public DateTimeOffset CreatedAt => Created;

    public string Payload => decoder.Decode(payload);

    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

    public ReadEventContext<string, StreamPosition, LogPosition> Context => ReadEventContext.Create(
        streamId,
        EventName,
        CreatedAt,
        StreamPosition.FromInt64(streamPosition),
        LogPosition.FromInt64(logPosition));

    public bool TryGetMetadata(string key, out ReadOnlySpan<char> value)
    {
        value = default;
        return false;
    }
}