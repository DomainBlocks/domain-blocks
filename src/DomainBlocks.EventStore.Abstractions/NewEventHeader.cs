namespace DomainBlocks.EventStore.Abstractions;

public sealed class NewEventHeader
{
    public NewEventHeader(string eventName, IDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        EventName = eventName;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string EventName { get; }
    public IDictionary<string, string> Metadata { get; }
}