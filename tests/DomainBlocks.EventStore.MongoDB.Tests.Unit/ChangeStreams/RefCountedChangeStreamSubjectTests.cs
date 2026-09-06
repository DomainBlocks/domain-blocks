using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit.ChangeStreams;

public class RefCountedChangeStreamSubjectTests
{
    [Test]
    public async Task Attach_FirstObserver_ConnectsSubject()
    {
        var subject = new TestSubject();
        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() => subject);

        await using var attachment = await refCountedSubject.AttachAsync(new TestObserver());

        subject.ConnectCount.ShouldBe(1);
        subject.AttachCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_MultipleObservers_SharesConnection()
    {
        var subject = new TestSubject();
        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() => subject);

        await using var attachment1 = await refCountedSubject.AttachAsync(new TestObserver());
        await using var attachment2 = await refCountedSubject.AttachAsync(new TestObserver());

        subject.ConnectCount.ShouldBe(1);
        subject.AttachCount.ShouldBe(2);
        subject.Connection!.DisposeCount.ShouldBe(0);
    }

    [Test]
    public async Task DisposeAsync_LastAttachment_DisposesConnection()
    {
        var subject = new TestSubject();
        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() => subject);

        var attachment1 = await refCountedSubject.AttachAsync(new TestObserver());
        var attachment2 = await refCountedSubject.AttachAsync(new TestObserver());

        await attachment1.DisposeAsync();

        subject.Connection!.DisposeCount.ShouldBe(0);
        subject.DetachCount.ShouldBe(1);

        await attachment2.DisposeAsync();

        subject.Connection.DisposeCount.ShouldBe(1);
        subject.DetachCount.ShouldBe(2);
    }

    [Test]
    public async Task DisposeAsync_DisposeMultipleTimes_DetachesOnlyOnce()
    {
        var subject = new TestSubject();
        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() => subject);
        var attachment = await refCountedSubject.AttachAsync(new TestObserver());

        await attachment.DisposeAsync();
        await attachment.DisposeAsync();

        subject.DetachCount.ShouldBe(1);
        subject.Connection!.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_AfterConnectionFault_ReplacesConnection()
    {
        var subjects = new List<TestSubject>();

        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() =>
        {
            var subject = new TestSubject();
            subjects.Add(subject);
            return subject;
        });

        var attachment1 = await refCountedSubject.AttachAsync(new TestObserver());
        var attachment2 = await refCountedSubject.AttachAsync(new TestObserver());
        var connection1 = subjects[0].Connection;
        connection1!.Fault(new InvalidOperationException());

        var attachment3 = await refCountedSubject.AttachAsync(new TestObserver());

        subjects.Count.ShouldBe(2);
        subjects[0].ConnectCount.ShouldBe(1);
        subjects[0].AttachCount.ShouldBe(2);
        subjects[1].ConnectCount.ShouldBe(1);
        subjects[1].AttachCount.ShouldBe(1);

        await attachment1.DisposeAsync();
        connection1.DisposeCount.ShouldBe(0);

        await attachment2.DisposeAsync();
        connection1.DisposeCount.ShouldBe(1);

        await attachment3.DisposeAsync();
        subjects[1].Connection!.DisposeCount.ShouldBe(1);
    }

    [Test]
    public async Task Attach_WhenConnectionFaultsDuringAttachment_PropagatesError()
    {
        var subject = new TestSubject();
        var refCountedSubject = new RefCountedChangeStreamSubject<int>(() => subject);
        var attachment = await refCountedSubject.AttachAsync(new TestObserver());
        var exception = new InvalidOperationException();
        subject.FaultOnNextAttach(exception);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            refCountedSubject.AttachAsync(new TestObserver()));

        thrown.ShouldBeSameAs(exception);

        subject.ConnectCount.ShouldBe(1);
        subject.AttachCount.ShouldBe(2);

        await attachment.DisposeAsync();
    }

    private sealed class TestSubject : IChangeStreamSubject<int>
    {
        private Exception? _attachException;

        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }
        public int ConnectCount { get; private set; }
        public TestConnection? Connection { get; private set; }

        public IDisposable Attach(IChangeStreamObserver<int> observer, string correlationId = "unknown")
        {
            AttachCount++;

            if (_attachException is { } exception)
            {
                _attachException = null;
                throw exception;
            }

            return new TestAttachment(() => DetachCount++);
        }

        public Task<IChangeStreamConnection> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            Connection = new TestConnection();
            return Task.FromResult<IChangeStreamConnection>(Connection);
        }

        public void FaultOnNextAttach(Exception exception) => _attachException = exception;
    }

    private sealed class TestConnection : IChangeStreamConnection
    {
        private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completionTcs.Task;

        public int DisposeCount { get; private set; }

        public void Fault(Exception exception) => _completionTcs.TrySetException(exception);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _completionTcs.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAttachment(Action onDetach) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                onDetach();
        }
    }

    private sealed class TestObserver : IChangeStreamObserver<int>
    {
        public ValueTask OnNextAsync(int change, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}