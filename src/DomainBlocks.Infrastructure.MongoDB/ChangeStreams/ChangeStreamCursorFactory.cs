using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

internal delegate Task<IChangeStreamCursor<TResult>> ChangeStreamCursorFactory<TDocument, TResult>(
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    ChangeStreamOptions? options,
    CancellationToken cancellationToken);