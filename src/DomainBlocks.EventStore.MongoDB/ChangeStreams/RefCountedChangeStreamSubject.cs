using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class RefCountedChangeStreamSubject
{
    public static RefCountedChangeStreamSubject<TResult> Create<TDocument, TResult>(
        ChangeStreamCursorFactory<TDocument, TResult> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TResult> pipeline,
        Func<TResult, BsonDocument> resumeTokenSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        return new RefCountedChangeStreamSubject<TResult>(() =>
            ChangeStreamSubject.Create(cursorFactory, pipeline, resumeTokenSelector, options, logger));
    }
}

/// <summary>
/// Connects the underlying subject when the first observer is attached and disconnects it when the last observer is
/// detached.
/// </summary>
internal sealed class RefCountedChangeStreamSubject<TDocument>(
    Func<IChangeStreamSubject<TDocument>> subjectFactory) :
    IRefCountedChangeStreamSubject<TDocument>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SubjectConnection? _currentSubjectConnection;

    public async Task<IAsyncDisposable> AttachAsync(
        IChangeStreamObserver<TDocument> observer,
        string correlationId = "unknown",
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SubjectConnection subjectConnection;
            IDisposable attachment;

            if (_currentSubjectConnection is null || _currentSubjectConnection.Connection.Completion.IsCompleted)
            {
                var subject = subjectFactory();
                var connection = await subject.ConnectAsync(cancellationToken);

                subjectConnection = new SubjectConnection(subject, connection);
                attachment = subject.Attach(observer, correlationId);

                _currentSubjectConnection = subjectConnection;
            }
            else
            {
                subjectConnection = _currentSubjectConnection;
                attachment = subjectConnection.Subject.Attach(observer, correlationId);
            }

            subjectConnection.RefCount++;

            return new AsyncDisposable(() => DetachAsync(attachment, subjectConnection));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachAsync(IDisposable attachment, SubjectConnection subjectConnection)
    {
        attachment.Dispose();
        IChangeStreamConnection? connectionToDispose = null;

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            subjectConnection.RefCount--;

            if (subjectConnection.RefCount == 0)
            {
                if (ReferenceEquals(_currentSubjectConnection, subjectConnection))
                    _currentSubjectConnection = null;

                connectionToDispose = subjectConnection.Connection;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (connectionToDispose is not null)
            await connectionToDispose.DisposeAsync();
    }

    private sealed class SubjectConnection(
        IChangeStreamSubject<TDocument> subject,
        IChangeStreamConnection connection)
    {
        public IChangeStreamSubject<TDocument> Subject { get; } = subject;
        public IChangeStreamConnection Connection { get; } = connection;
        public int RefCount { get; set; }
    }

    private sealed class AsyncDisposable(Func<Task> onDispose) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose();
        }
    }
}