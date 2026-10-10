using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

using Observer = SubscriptionAsyncEnumerable<string, LogPosition>.Observer;

public class SubscriptionObserverTests
{
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
    public async Task OnNextAsync_WhenQueueIsFull_SignalsOverflow()
    {
        using var observer = new Observer(queueCapacity: 1, EventFilter.All);

        await OfferAsync(observer, 0, "order-1");
        observer.OverflowToken.IsCancellationRequested.ShouldBeFalse();

        await OfferAsync(observer, 1, "order-1");

        observer.OverflowToken.IsCancellationRequested.ShouldBeTrue();
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
        observer.OverflowToken.IsCancellationRequested.ShouldBeFalse();

        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observer.Reader.WaitToReadAsync());

        exception.Message.ShouldBe("Cannot decode.");
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
}