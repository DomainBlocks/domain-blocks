namespace DomainBlocks.MongoDB.Sequencing;

public interface IMongoSequencedAppender<in TDocument, in TContext> : IAsyncDisposable
{
    Task AppendAsync(
        IEnumerable<TDocument> documents,
        TContext context,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default);
}