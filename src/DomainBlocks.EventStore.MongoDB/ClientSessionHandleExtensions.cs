using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal static class ClientSessionHandleExtensions
{
    extension(IClientSessionHandle session)
    {
        public async Task CommitWithRetryOnUnknownResultAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                try
                {
                    await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (MongoException ex) when (ex.HasErrorLabel(MongoErrorLabels.UnknownTransactionCommitResult))
                {
                    // Retry
                }
            }
        }
    }
}