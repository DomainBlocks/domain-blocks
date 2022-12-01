namespace DomainBlocks.Projections.New;

public static class StreamPosition
{
    public static readonly IStreamPosition Empty = new EmptyStreamPosition();

    public static IStreamPosition FromJsonString(string json)
    {
        return new JsonStringStreamPosition(json);
    }
}

public interface IStreamPosition
{
    string ToJsonString();
}

internal sealed class EmptyStreamPosition : IStreamPosition
{
    public string ToJsonString() => "{}";
}

internal sealed class JsonStringStreamPosition : IStreamPosition
{
    public JsonStringStreamPosition(string json)
    {
        Json = json;
    }

    public string Json { get; }

    public string ToJsonString() => Json;
}