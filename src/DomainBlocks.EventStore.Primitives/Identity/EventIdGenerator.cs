namespace DomainBlocks.EventStore.Primitives.Identity;

public static class EventIdGenerator
{
    public static Guid Generate(string streamId, Guid commitId, int commitIndex)
    {
        var name = $"stream:{streamId}/commit:{commitId:N}/i:{commitIndex}";
        return Guid.CreateVersion5(NamespaceIds.Events, name);
    }
}