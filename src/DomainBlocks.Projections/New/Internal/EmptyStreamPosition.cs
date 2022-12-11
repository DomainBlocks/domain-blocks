namespace DomainBlocks.Projections.New.Internal;

internal sealed class EmptyStreamPosition : IStreamPosition
{
    public static readonly IStreamPosition Instance = new EmptyStreamPosition();

    private EmptyStreamPosition()
    {
    }

    public string ToJsonString() => "{}";
}