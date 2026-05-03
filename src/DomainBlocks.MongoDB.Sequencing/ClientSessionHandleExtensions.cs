using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

internal static class ClientSessionHandleExtensions
{
    extension(IClientSessionHandle session)
    {
        public async Task CommitWithRetryOnUnknownResultAsync(
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
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
                    logger?.LogWarning(ex, "Unknown transaction commit result; retrying");
                }
            }
        }
    }
}