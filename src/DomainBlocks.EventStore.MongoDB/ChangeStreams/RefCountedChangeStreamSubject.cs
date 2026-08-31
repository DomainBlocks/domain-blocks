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
    private readonly Lock _gate = new();
    private SubjectConnection? _currentSubjectConnection;

    public IAsyncDisposable Attach(IChangeStreamObserver<TDocument> observer)
    {
        lock (_gate)
        {
            SubjectConnection subjectConnection;
            IDisposable attachment;

            if (_currentSubjectConnection is null || _currentSubjectConnection.Connection.Completion.IsFaulted)
            {
                (subjectConnection, attachment) = CreateAndConnectSubject(observer);
                _currentSubjectConnection = subjectConnection;
            }
            else
            {
                subjectConnection = _currentSubjectConnection;
                attachment = subjectConnection.Subject.Attach(observer);
            }

            subjectConnection.RefCount++;

            return new AsyncDisposable(() => DetachAsync(attachment, subjectConnection));
        }
    }

    private (SubjectConnection SubjectConnection, IDisposable Attachment) CreateAndConnectSubject(
        IChangeStreamObserver<TDocument> observer)
    {
        var subject = subjectFactory();
        var attachment = subject.Attach(observer); // Attach first so no notifications are missed
        return (new SubjectConnection(subject, subject.Connect()), attachment);
    }

    private ValueTask DetachAsync(IDisposable attachment, SubjectConnection subjectConnection)
    {
        attachment.Dispose();
        IChangeStreamConnection? connectionToDispose = null;

        lock (_gate)
        {
            subjectConnection.RefCount--;

            if (subjectConnection.RefCount == 0)
            {
                if (ReferenceEquals(_currentSubjectConnection, subjectConnection))
                    _currentSubjectConnection = null;

                connectionToDispose = subjectConnection.Connection;
            }
        }

        return connectionToDispose?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class SubjectConnection(IChangeStreamSubject<TDocument> subject, IChangeStreamConnection connection)
    {
        public IChangeStreamSubject<TDocument> Subject { get; } = subject;
        public IChangeStreamConnection Connection { get; } = connection;
        public int RefCount { get; set; }
    }

    private sealed class AsyncDisposable(Func<ValueTask> onDispose) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _disposed, 1) == 0
            ? onDispose()
            : ValueTask.CompletedTask;
    }
}