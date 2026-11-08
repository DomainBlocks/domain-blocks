namespace DomainBlocks.EventStore.Internal;

internal static class AppendError
{
    public static InvalidOperationException CommitIdInProgress(Guid commitId) =>
        new($"Another append with commit ID '{commitId}' is still in progress.");
}