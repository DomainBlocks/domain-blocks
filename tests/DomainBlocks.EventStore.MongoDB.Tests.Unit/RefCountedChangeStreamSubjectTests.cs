using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class RefCountedChangeStreamSubjectTests
{
    [Test]
    [CancelAfter(5000)]
    public async Task AttachAsync_WhenTheLastConnectionIsStillStopping_WaitsForItBeforeItConnectsAgain()
    {
        // A connection that is stopping may still be handing a change to its observers. The store keeps one document
        // for the observers of a change to share, so two connections must never hand out changes at once.
        var connections = new List<FakeConnection>();

        var subject = new RefCountedChangeStreamSubject<int>(() => new FakeSubject(connections));

        var first = await subject.AttachAsync(new FakeObserver());
        var detaching = first.DisposeAsync().AsTask();

        await connections[0].Stopping;

        var attaching = subject.AttachAsync(new FakeObserver());

        connections.Count.ShouldBe(1);

        connections[0].Stop();

        await detaching;
        var second = await attaching;

        connections.Count.ShouldBe(2);

        connections[1].Stop();
        await second.DisposeAsync();
    }

    private sealed class FakeSubject(List<FakeConnection> connections) : IChangeStreamSubject<int>
    {
        public IDisposable Attach(IChangeStreamObserver<int> observer, string correlationId = "unknown") =>
            new Attachment();

        public Task<IChangeStreamConnection> ConnectAsync(CancellationToken cancellationToken = default)
        {
            var connection = new FakeConnection();
            connections.Add(connection);

            return Task.FromResult<IChangeStreamConnection>(connection);
        }
    }

    private sealed class FakeConnection : IChangeStreamConnection
    {
        private readonly TaskCompletionSource _stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Stopping => _stopping.Task;

        public Task Completion => _stopped.Task;

        public BsonTimestamp OperationTime { get; } = new(0);

        public void Stop() => _stopped.SetResult();

        public async ValueTask DisposeAsync()
        {
            _stopping.SetResult();
            await _stopped.Task;
        }
    }

    private sealed class Attachment : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeObserver : IChangeStreamObserver<int>
    {
        public ValueTask OnNextAsync(int change, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}