using System.Diagnostics.CodeAnalysis;

namespace DomainBlocks.EventStore.Filtering;

internal sealed class FilterableReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> : IFilterableEvent
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
{
    private ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> _event;

    public string EventName => _event.Context.EventName;

    public string StreamId => _event.Context.StreamId.ToString()!;

    public DateTimeOffset CreatedAt => _event.Context.CreatedAt;

    public object DecodedPayload => _event.Payload;

    public void Set(ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> @event) => _event = @event;

    public bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value) =>
        _event.Context.Metadata.TryGetValue(key, out value);
}