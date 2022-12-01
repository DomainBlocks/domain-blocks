namespace DomainBlocks.Projections.New;

public static class StreamPosition
{
    public static readonly IStreamPosition Empty = EmptyStreamPosition.Instance;

    public static IStreamPosition FromJsonString(string json)
    {
        return new JsonStringStreamPosition(json);
    }
}