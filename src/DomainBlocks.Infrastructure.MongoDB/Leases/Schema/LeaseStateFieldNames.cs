namespace DomainBlocks.Infrastructure.MongoDB.Leases.Schema;

public static class LeaseStateFieldNames
{
    public const string ResourceId = "_id";
    public const string HolderId = "holderId";
    public const string Epoch = "epoch";
    public const string ContentionPriority = "contentionPriority";
    public const string HeldSinceUtc = "heldSinceUtc";
    public const string ExpiresAtUtc = "expiresAtUtc";
    public const string Counters = "counters";
    public const string LastMutation = "lastMutation";
}