using DomainBlocks.EventStore.Codecs;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

/// <summary>
/// Decodes an event to its name, and its metadata to the entries of the stored document as text, counting the calls.
/// </summary>
internal sealed class CountingDecoder : IEventDecoder<string, BsonValue, BsonValue>
{
    public int DecodeCount { get; private set; }

    public Exception? Failure { get; set; }

    public DecodedEvent<string> Decode(string eventName, BsonValue eventData, BsonValue? metadata)
    {
        DecodeCount++;

        if (Failure is not null)
            throw Failure;

        var decodedMetadata = metadata is BsonDocument entries
            ? entries.ToDictionary(x => x.Name, x => x.Value.ToString()!)
            : [];

        return DecodedEvent.Create(eventName, decodedMetadata);
    }
}