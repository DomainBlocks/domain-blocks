using System.Net;
using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

using Observer = SubscriptionAsyncEnumerable<string, LogPosition>.Observer;
using StopReason = SubscriptionAsyncEnumerable<string, LogPosition>.StopReason;

public class SubscriptionObserverTests
{
    private const int ChangeStreamHistoryLostCode = 286;
    private const int UnauthorizedCode = 13;

    private static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private CountingDecoder _decoder = null!;
    private EventLogDocument<string> _document = null!;

    [SetUp]
    public void SetUp()
    {
        _decoder = new CountingDecoder();
        _document = new EventLogDocument<string>(_decoder);
    }

    [Test]
    public async Task OnNextAsync_SelectedDocument_QueuesItsEvent()
    {
        using var observer = new Observer(queueCapacity: 2, EventFilter.StreamIds("order-1"));

        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-2");
        await OfferAsync(observer, 2, "order-1");

        observer.Reader.TryRead(out var first).ShouldBeTrue();
        observer.Reader.TryRead(out var second).ShouldBeTrue();
        observer.Reader.TryRead(out _).ShouldBeFalse();
        first.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        second.Context.LogPosition.ShouldBe(LogPosition.FromInt64(2));
        _decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_SignalsRestart()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);

        await OfferAsync(observer, 0, "order-1");
        observer.StopReason.ShouldBe(StopReason.None);

        await OfferAsync(observer, 1, "order-1");

        observer.StopReason.ShouldBe(StopReason.QueueOverflow);
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_EndsReaderOnceQueuedEventIsRead()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        await OfferAsync(observer, 1, "order-1");

        (await observer.Reader.WaitToReadAsync()).ShouldBeTrue();
        observer.Reader.TryRead(out var queued).ShouldBeTrue();
        queued.Context.LogPosition.ShouldBe(LogPosition.FromInt64(0));
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task OnNextAsync_WhenQueueIsFull_LogsOverflowAsWarningOnceWhenItHappens()
    {
        var logger = new RecordingLogger();
        using var observer = new Observer(queueCapacity: 1, EventFilter.All, logger, "sub-1");

        await OfferAsync(observer, 0, "order-1");
        logger.Messages.ShouldBeEmpty();

        await OfferAsync(observer, 1, "order-1");
        await OfferAsync(observer, 2, "order-1");

        logger.Messages.ShouldBe(
            [(LogLevel.Warning, "[sub: sub-1] queue overflowed (capacity 1); restart pending")]);
    }

    [Test]
    public async Task Dispose_WhenAlreadyStoppedByQueueOverflow_KeepsFirstReason()
    {
        var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");

        observer.Dispose();

        observer.StopReason.ShouldBe(StopReason.QueueOverflow);
    }

    [Test]
    public async Task Dispose_WhenNotYetStopped_EndsReaderOnceQueuedEventIsRead()
    {
        var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        observer.Dispose();

        observer.StopReason.ShouldBe(StopReason.Disposed);
        observer.Reader.TryRead(out _).ShouldBeTrue();
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task OnNextAsync_AfterQueueOverflowed_DoesNotDecodeFurtherDocuments()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");
        var decodeCountAtOverflow = _decoder.DecodeCount;

        await OfferAsync(observer, 2, "order-1");
        await OfferAsync(observer, 3, "order-1");

        _decoder.DecodeCount.ShouldBe(decodeCountAtOverflow);
    }

    [Test]
    public async Task OnNextAsync_AfterDocumentCouldNotBeDecoded_FailsReaderAndDoesNotDecodeFurtherDocuments()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        _decoder.Failure = new InvalidOperationException("Cannot decode.");
        await OfferAsync(observer, 0, "order-1");

        _decoder.Failure = null;
        await OfferAsync(observer, 1, "order-1");

        _decoder.DecodeCount.ShouldBe(1);
        observer.StopReason.ShouldBe(StopReason.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.Message.ShouldBe("Cannot decode.");
    }

    [Test]
    public async Task OnErrorAsync_WhenHistoryLost_SignalsRestart()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");

        await observer.OnErrorAsync(CreateCommandException(ChangeStreamHistoryLostCode), CancellationToken.None);

        observer.StopReason.ShouldBe(StopReason.ChangeStreamHistoryLost);
        observer.Reader.TryRead(out _).ShouldBeTrue();
        (await observer.Reader.WaitToReadAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task OnErrorAsync_WhenHistoryLost_LogsWarningOnce()
    {
        var logger = new RecordingLogger();
        using var observer = new Observer(queueCapacity: 10, EventFilter.All, logger, "sub-1");

        await observer.OnErrorAsync(CreateCommandException(ChangeStreamHistoryLostCode), CancellationToken.None);
        await observer.OnErrorAsync(CreateCommandException(ChangeStreamHistoryLostCode), CancellationToken.None);

        logger.Messages.ShouldBe(
            [(LogLevel.Warning, "[sub: sub-1] change stream history was lost; restart pending")]);
    }

    [Test]
    public async Task OnErrorAsync_WhenHistoryLostAfterQueueOverflowed_KeepsFirstReason()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);
        await OfferAsync(observer, 0, "order-1");
        await OfferAsync(observer, 1, "order-1");

        await observer.OnErrorAsync(CreateCommandException(ChangeStreamHistoryLostCode), CancellationToken.None);

        observer.StopReason.ShouldBe(StopReason.QueueOverflow);
    }

    [Test]
    public async Task OnErrorAsync_WhenOtherError_FailsReader()
    {
        using var observer = new Observer(queueCapacity: 10, EventFilter.All);
        var error = CreateCommandException(UnauthorizedCode);

        await observer.OnErrorAsync(error, CancellationToken.None);

        observer.StopReason.ShouldBe(StopReason.None);

        var exception = await Should.ThrowAsync<MongoCommandException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.ShouldBeSameAs(error);
    }

    private static MongoCommandException CreateCommandException(int code)
    {
        var serverId = new ServerId(new ClusterId(), new IPEndPoint(IPAddress.Loopback, 27017));

        return new MongoCommandException(
            new ConnectionId(serverId),
            "The command failed.",
            new BsonDocument("getMore", 1),
            new BsonDocument { { "ok", 0 }, { "code", code } });
    }

    private ValueTask OfferAsync(Observer observer, long position, string streamId)
    {
        _document.Set(new BsonDocument
        {
            { EventLogEntry.FieldNames.Position, position },
            { EventLogEntry.FieldNames.StreamId, streamId },
            { EventLogEntry.FieldNames.StreamPosition, position },
            { EventLogEntry.FieldNames.EventName, "OrderPlaced" },
            { EventLogEntry.FieldNames.EventData, new BsonDocument() },
            { EventLogEntry.FieldNames.Metadata, BsonNull.Value },
            { EventLogEntry.FieldNames.CreatedAtUtc, CreatedAtUtc }
        });

        return observer.OnNextAsync(_document, CancellationToken.None);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}