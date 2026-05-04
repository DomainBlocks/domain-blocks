namespace DomainBlocks.MongoDB.Sequencing;

internal sealed class AppendCompletionSource<TContext> : IAppendCompletionSource<TContext>
{
    public static readonly AppendCompletionSource<TContext> Shared = new();

    public bool TryComplete(AppendEntry<TContext> appendEntry, Exception? error = null)
    {
        return appendEntry.TryComplete(error);
    }
}