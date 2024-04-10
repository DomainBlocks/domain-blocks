namespace DomainBlocks.Persistence;

public static class DefaultStreamNamePrefix
{
    public static string CreateFor(Type entityType)
    {
        var name = entityType.Name;
        return $"{name[..1].ToLower()}{name[1..]}";
    }
}