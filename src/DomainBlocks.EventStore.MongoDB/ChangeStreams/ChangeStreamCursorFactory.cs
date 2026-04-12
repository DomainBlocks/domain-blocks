using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal delegate Task<IChangeStreamCursor<TResult>> ChangeStreamCursorFactory<TDocument, TResult>(
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    ChangeStreamOptions? options,
    CancellationToken cancellationToken);