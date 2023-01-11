namespace DomainBlocks.Core.Serialization;

public interface IReadEventAdapter<in TEvent>
{
    string GetEventName(TEvent @event);

    Task<object> DeserializeEvent(
        TEvent @event,
        Type eventType,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> DeserializeMetadata(TEvent @event);
}