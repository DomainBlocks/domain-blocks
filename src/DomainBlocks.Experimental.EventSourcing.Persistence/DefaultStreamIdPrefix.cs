namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public static class DefaultStreamIdPrefix
{
    public static string CreateFor(Type entityType)
    {
        var name = entityType.Name;
        return $"{name[..1].ToLower()}{name[1..]}";
    }
}