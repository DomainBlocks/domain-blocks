using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public sealed class ChangeStreamSubject<TDocument, TResult>(
    ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
    PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
    Func<TResult, BsonDocument> resumeTokenSelector,
    ChangeStreamSubscriptionOptions? options,
    ILogger? logger) :
    IChangeStreamSubject<TResult>
{
    private readonly ChangeStreamSubscriptionOptions _options = options ?? new ChangeStreamSubscriptionOptions();
    private readonly ObserverRegistry _observers = new();
    private int _connected;

    public IDisposable Attach(IChangeStreamObserver<TResult> observer) => _observers.Attach(observer);

    public IAsyncDisposable ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Interlocked.Exchange(ref _connected, 1) == 0
            ? new Connection(cursorFactory, pipeline, resumeTokenSelector, _options, logger, _observers)
            : throw new InvalidOperationException("ConnectAsync may only be called once.");
    }

    private sealed class ObserverRegistry
    {
        private ImmutableArray<IChangeStreamObserver<TResult>> _observers = [];

        public IDisposable Attach(IChangeStreamObserver<TResult> observer)
        {
            ImmutableInterlocked.Update(
                ref _observers,
                (observers, o) => observers.Add(o),
                observer);

            return new ObserverAttachment(this, observer);
        }

        public void Detach(IChangeStreamObserver<TResult> observer)
        {
            ImmutableInterlocked.Update(
                ref _observers,
                (observers, o) => observers.Remove(o),
                observer);
        }

        public ImmutableArray<IChangeStreamObserver<TResult>> Snapshot() => _observers;
    }

    private sealed class ObserverAttachment(ObserverRegistry registry, IChangeStreamObserver<TResult> observer) :
        IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                registry.Detach(observer);
        }
    }

    private sealed class Connection(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubscriptionOptions? options,
        ILogger? logger,
        ObserverRegistry observers) :
        IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }
    }
}