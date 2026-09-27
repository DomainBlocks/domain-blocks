using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.ChangeStreams;

internal static class RefCountedChangeStreamSubject
{
    public static RefCountedChangeStreamSubject<TResult> Create<TDocument, TChange, TResult>(
        IMongoClient mongoClient,
        ChangeStreamCursorFactory<TDocument, TChange> cursorFactory,
        PipelineDefinition<ChangeStreamDocument<TDocument>, TChange> pipeline,
        Func<TChange, BsonDocument> resumeTokenSelector,
        Func<TChange, TResult> resultSelector,
        ChangeStreamSubjectOptions? options = null,
        ILogger? logger = null)
    {
        return new RefCountedChangeStreamSubject<TResult>(() => ChangeStreamSubject.Create(
            mongoClient,
            cursorFactory,
            pipeline,
            resumeTokenSelector,
            resultSelector,
            options,
            logger));
    }
}

/// <summary>
/// Connects the underlying subject when the first observer is attached and disconnects it when the last observer is
/// detached.
/// </summary>
internal sealed class RefCountedChangeStreamSubject<TResult>(
    Func<IChangeStreamSubject<TResult>> subjectFactory) :
    IRefCountedChangeStreamSubject<TResult>
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SubjectConnection? _currentSubjectConnection;

    public async Task<IChangeStreamAttachment> AttachAsync(
        IChangeStreamObserver<TResult> observer,
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

            return new Attachment(
                subjectConnection.Connection.OperationTime,
                () => DetachAsync(attachment, subjectConnection));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachAsync(IDisposable attachment, SubjectConnection subjectConnection)
    {
        attachment.Dispose();

        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            subjectConnection.RefCount--;

            if (subjectConnection.RefCount > 0)
                return;

            if (ReferenceEquals(_currentSubjectConnection, subjectConnection))
                _currentSubjectConnection = null;

            // Within the gate, so that the next connection is not made until this one has handed out its last
            // change. Observers may share what they make of a change, which two connections at once would corrupt.
            await subjectConnection.Connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class SubjectConnection(
        IChangeStreamSubject<TResult> subject,
        IChangeStreamConnection connection)
    {
        public IChangeStreamSubject<TResult> Subject { get; } = subject;
        public IChangeStreamConnection Connection { get; } = connection;
        public int RefCount { get; set; }
    }

    private sealed class Attachment(BsonTimestamp operationTime, Func<Task> onDispose) : IChangeStreamAttachment
    {
        private int _disposed;

        public BsonTimestamp OperationTime { get; } = operationTime;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await onDispose();
        }
    }
}