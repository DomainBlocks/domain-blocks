namespace DomainBlocks.Persistence;

public static class StreamVersion
{
    public const long Any = long.MinValue + 1;
    public const long NewStream = long.MinValue + 2;
    public const long NoStream = -1;
}