using System.Text.Json;

namespace DomainBlocks.Projections.SqlStreamStore;

internal sealed class AllStreamPosition : IStreamPosition
{
    public AllStreamPosition(long? position)
    {
        Position = position;
    }

    public long? Position { get; }

    public static AllStreamPosition From(long? position) => new(position);

    public static AllStreamPosition FromJsonString(string json)
    {
        return JsonSerializer.Deserialize<AllStreamPosition>(json);
    }

    public string ToJsonString()
    {
        return JsonSerializer.Serialize(this);
    }
}