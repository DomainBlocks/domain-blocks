using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

public class WrongExpectedVersionExceptionTests
{
    private const int TestTimeoutMillis = 5_000;

    // ReSharper disable once NullableWarningSuppressionIsUsed This is initialized in OneTimeSetUp
    private IKurrentDbEventStore _eventStore = null!;

    [SetUp]
    public void OneTimeSetUp()
    {
        var client = new KurrentDBClient(
            KurrentDBClientSettings.Create(
                "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false&gossipTimeout=5000&keepAliveTimeout=5000")
        );

        _eventStore = new KurrentDbEventStore(client);
    }

    [Test]
    [CancelAfter(5000)]
    public Task AppendToStreamAsync_WhenVersionConflict_WrongExpectedVersionConflictThrown(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Uuid.NewUuid()}";
        var events = new[]
        {
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        };

        var wrongExpectedStreamStateException = Assert.ThrowsAsync<WrongExpectedStreamStateException>(async () =>
            await _eventStore.AppendToStreamAsync(
                streamId,
                events,
                ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1)),
                cancellationToken));

        wrongExpectedStreamStateException.ShouldNotBeNull().Reason
            .ShouldBe(WrongExpectedStreamStateReason.VersionConflict);
        return Task.CompletedTask;
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public void
        AppendToStreamAsync_WhenExpectedStateStreamExistsAndStreamDoesNotExist_WrongExpectedStreamStateExceptionThrown(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Uuid.NewUuid()}";
        var events = new[]
        {
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        };

        var wrongExpectedStreamStateException = Assert.ThrowsAsync<WrongExpectedStreamStateException>(async () =>
            await _eventStore.AppendToStreamAsync(
                streamId,
                events,
                ExpectedStreamState.StreamExists,
                cancellationToken));

        wrongExpectedStreamStateException.ShouldNotBeNull().Reason
            .ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task
        AppendToStreamAsync_WhenExpectedStateStreamDoesNotExistAndStreamExists_WrongExpectedStreamStateExceptionThrown(
            CancellationToken cancellationToken)
    {
        var streamId = $"test-{Uuid.NewUuid()}";
        var events = new[]
        {
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        };

        await _eventStore.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any,
            cancellationToken);

        var wrongExpectedStreamStateException = await _eventStore.AppendToStreamAsync(
            streamId,
            [TestEventsHelper.CreateTestEvent("TestEvent4")],
            ExpectedStreamState.StreamDoesNotExist,
            cancellationToken).ShouldThrowAsync<WrongExpectedStreamStateException>();

        wrongExpectedStreamStateException.ShouldNotBeNull().Reason
            .ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToNotExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenVersionConflicts_ActualVersionExceptionThrown()
    {
        var streamId = $"test-{Uuid.NewUuid()}";
        var events = new[]
        {
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        };

        await _eventStore.AppendToStreamAsync(
            streamId,
            events,
            ExpectedStreamState.Any);

        var wrongExpectedStreamStateException = await _eventStore.AppendToStreamAsync(
                streamId,
                [TestEventsHelper.CreateTestEvent("TestEvent4")],
                ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1)))
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        wrongExpectedStreamStateException.ShouldNotBeNull().ActualVersion
            .ShouldBe(StreamVersion.FromInt64(2));
    }
}