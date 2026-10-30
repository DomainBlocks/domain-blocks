using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Feeds;

internal delegate Task<IChangeStreamCursor<TResult>> ChangeStreamCursorFactory<TDocument, TResult>(
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    ChangeStreamOptions? options,
    CancellationToken cancellationToken);