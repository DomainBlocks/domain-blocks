using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDb.Tests.Integration;

public class WrongExpectedStreamStateExceptionTests
{
    private const int TestTimeoutMillis = 5_000;

    // ReSharper disable once NullableWarningSuppressionIsUsed This is initialized in OneTimeSetUp
    private IKurrentDbEventStoreAdapter _adapter = null!;

    [SetUp]
    public void OneTimeSetUp()
    {
        const string connectionString =
            "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false&gossipTimeout=5000&keepAliveTimeout=5000";

        var client = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        _adapter = new KurrentDbEventStoreAdapter(client);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenVersionConflict_VersionConflictThrown(CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        await _adapter.AppendToStreamAsync(
            streamId,
            [
                TestEventsHelper.CreateTestEvent("TestEvent1"),
                TestEventsHelper.CreateTestEvent("TestEvent2"),
                TestEventsHelper.CreateTestEvent("TestEvent3")
            ],
            cancellationToken: cancellationToken);

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [TestEventsHelper.CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions
                {
                    ExpectedState = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1))
                },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.VersionConflict);
        exception.ActualVersion.ShouldBe(StreamVersion.FromInt64(2));
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenStreamExistsExpectedAndStreamDoesNotExist_ExpectedStreamToExistThrown(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [
                    TestEventsHelper.CreateTestEvent("TestEvent1"),
                    TestEventsHelper.CreateTestEvent("TestEvent2"),
                    TestEventsHelper.CreateTestEvent("TestEvent3")
                ],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamExists },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToExist);
    }

    [Test]
    [CancelAfter(TestTimeoutMillis)]
    public async Task AppendToStreamAsync_WhenStreamDoesNotExistExpectedAndStreamExists_StreamDoesNotExistThrown(
        CancellationToken cancellationToken)
    {
        var streamId = $"test-{Guid.NewGuid()}";

        var events = new[]
        {
            TestEventsHelper.CreateTestEvent("TestEvent1"),
            TestEventsHelper.CreateTestEvent("TestEvent2"),
            TestEventsHelper.CreateTestEvent("TestEvent3")
        };

        await _adapter.AppendToStreamAsync(streamId, events, cancellationToken: cancellationToken);

        var exception = await _adapter
            .AppendToStreamAsync(
                streamId,
                [TestEventsHelper.CreateTestEvent("TestEvent4")],
                new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
                cancellationToken)
            .ShouldThrowAsync<WrongExpectedStreamStateException>();

        exception.Reason.ShouldBe(WrongExpectedStreamStateReason.ExpectedStreamToNotExist);
    }
}