using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace DomainBlocks.EventStore.Filtering;

public abstract class FilterableEvent<TEvent, TEventData, TMetadata> : IFilterableEvent
    where TEvent : notnull
    where TEventData : notnull
{
    private ReadEvent<TEvent, string, StreamPosition, LogPosition> _event;
    private bool _isDecoded;
    private ExceptionDispatchInfo? _decodeError;

    public string EventName { get; private set; } = string.Empty;
    public string StreamId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodedEvent => _isDecoded ? _event : DecodeImpl();

    protected void Reset(string eventName, string streamId, DateTimeOffset createdAt)
    {
        EventName = eventName;
        StreamId = streamId;
        CreatedAt = createdAt;

        _event = default;
        _isDecoded = false;
        _decodeError = null;
    }

    public abstract bool TryGetMetadata(string key, [MaybeNullWhen(false)] out string value);

    protected abstract ReadEvent<TEvent, string, StreamPosition, LogPosition> Decode();

    private ReadEvent<TEvent, string, StreamPosition, LogPosition> DecodeImpl()
    {
        _decodeError?.Throw();

        try
        {
            _event = Decode();
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