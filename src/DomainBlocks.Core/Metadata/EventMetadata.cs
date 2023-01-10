namespace DomainBlocks.Core.Metadata;

public class EventMetadata : Dictionary<string, string>
{
    public static EventMetadata FromKeyValuePairs(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var metadata = new EventMetadata();
        foreach (var (key, value) in entries)
        {
            metadata.Add(key, value);
        }

        return metadata;
    }
}