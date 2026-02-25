using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public delegate Task<IChangeStreamCursor<TResult>> ChangeStreamCursorFactory<TDocument, TResult>(
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    ChangeStreamOptions? options,
    CancellationToken cancellationToken);