namespace DomainBlocks.Projections.New;

internal sealed class JsonStringStreamPosition : IStreamPosition
{
    public JsonStringStreamPosition(string json)
    {
        Json = json;
    }

    public string Json { get; }

    public string ToJsonString() => Json;
}