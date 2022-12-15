using System.Collections.Generic;

namespace DomainBlocks.Serialization;

public class EventMetadata : Dictionary<string, string>
{
    public static readonly EventMetadata Empty = new();

    public static EventMetadata FromKeyValuePairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var metadata = new EventMetadata();
        foreach (var (key, value) in pairs)
        {
            metadata.Add(key, value);
        }

        return metadata;
    }
}