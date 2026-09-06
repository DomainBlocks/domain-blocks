namespace DomainBlocks.EventStore.MongoDB;

internal static class CorrelationId
{
    public const int IdLength = 6;

    private static readonly Lock Gate = new();
    private static readonly HashSet<string> ReservedIds = new(StringComparer.Ordinal);

    public static string Reserve(string id)
    {
        return TryReserve(id)
            ? id
            : throw new InvalidOperationException($"The correlation ID '{id}' is already reserved.");
    }

    public static bool TryReserve(string id)
    {
        lock (Gate)
            return ReservedIds.Add(id);
    }

    public static string ReserveGenerated()
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..IdLength];
            if (TryReserve(id))
                return id;
        }
    }

    public static void Release(string id)
    {
        lock (Gate)
            ReservedIds.Remove(id);
    }
}