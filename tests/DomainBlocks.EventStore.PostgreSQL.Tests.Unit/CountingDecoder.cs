using DomainBlocks.EventStore.Codecs;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

/// <summary>
/// Decodes an event to its name and data, and its metadata to one entry holding the stored text, counting the calls.
/// </summary>
internal sealed class CountingDecoder : IEventDecoder<string, PostgresEventData, string>
{
    public int DecodeCount { get; private set; }

    public Exception? Failure { get; set; }

    public DecodedEvent<string> Decode(string eventName, PostgresEventData eventData, string? metadata)
    {
        DecodeCount++;

        if (Failure is not null)
            throw Failure;

        var decodedMetadata = new Dictionary<string, string>();

        if (metadata is not null)
            decodedMetadata["raw"] = metadata;

        return DecodedEvent.Create($"{eventName}:{eventData.Json}", decodedMetadata);
    }
}