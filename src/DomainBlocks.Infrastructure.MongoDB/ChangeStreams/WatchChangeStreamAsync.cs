using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public delegate Task<IChangeStreamCursor<TResult>> WatchChangeStreamAsync<TDocument, TResult>(
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    ChangeStreamOptions? options,
    CancellationToken cancellationToken);