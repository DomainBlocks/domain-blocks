namespace DomainBlocks.Core.Serialization;

public interface IReadEventConverter<in TEvent>
{
    Task<object> DeserializeEvent(
        TEvent @event,
        Type? eventTypeOverride = null,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> DeserializeMetadata(TEvent @event);
}