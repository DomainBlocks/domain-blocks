namespace DomainBlocks.Persistence.Events.Abstractions;

public sealed class EventData<TPayload>
{
    public EventData(
        string eventName,
        TPayload payload,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        EventName = eventName;
        Payload = payload;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string EventName { get; }
    public TPayload Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}