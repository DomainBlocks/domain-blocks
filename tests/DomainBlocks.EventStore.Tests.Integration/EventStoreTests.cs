using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.Serialization.SystemTextJson;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Integration;

public class EventStoreTests
{
    private static readonly MongoEventStoreOptions<EventDocument<string>, string> MongoEventStoreOptions =
        MongoDB.MongoEventStoreOptions.CreateDefault<string>();

    private IEventStoreBackend<string> _eventStoreBackend = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var client = new MongoClient("mongodb://localhost:27017");
        var mongoDatabase = client.GetDatabase("test");
        await MongoEventStoreAdmin.EnsureIndexesAsync(mongoDatabase, MongoEventStoreOptions);

        _eventStoreBackend = MongoEventStore.Create(mongoDatabase, MongoEventStoreOptions);
    }

    [Test]
    public async Task Should_allow_reading_multiple_events_as_common_type()
    {
        var writeEventTypeMap = new EventTypeMapBuilder()
            .MapType<TradeCaptured>()
            .MapType<TradeAmended>()
            .MapType<TradeBooked>()
            .Build();

        var readEventTypeMap = new EventTypeMapBuilder()
            .MapReadType<TradeEvent>(nameof(TradeCaptured), nameof(TradeAmended), nameof(TradeBooked))
            .Build();

        var writeEventStoreOptions = new EventStoreOptions<string>
        {
            Backend = _eventStoreBackend,
            TypeMap = writeEventTypeMap,
            Serializer = new SystemTextJsonStringSerializer()
        };

        var readEventStoreOptions = new EventStoreOptions<string>
        {
            Backend = _eventStoreBackend,
            TypeMap = readEventTypeMap,
            Serializer = new SystemTextJsonStringSerializer()
        };

        var writeEventStore = new EventStore<string>(writeEventStoreOptions);
        var readEventStore = new EventStore<string>(readEventStoreOptions);

        var tradeId = Guid.NewGuid();
        var streamId = $"trade-{tradeId}";

        var tradeCaptured = new TradeCaptured
        {
            TradeId = tradeId,
            Amount = 10_000,
            Currency = "EUR",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero),
            CreatedBy = "Bob"
        };

        var tradeAmended = new TradeAmended
        {
            TradeId = tradeId,
            Amount = 15_000,
            Currency = "CHF",
            AmendedAt = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
            AmendedBy = "Alice"
        };

        var tradeBooked = new TradeBooked
        {
            TradeId = tradeId,
            Amount = 15_000,
            Currency = "CHF",
            BookedAt = new DateTimeOffset(2025, 1, 1, 11, 0, 0, TimeSpan.Zero),
            BookedBy = "Joe",
            Counterparty = "Some Company Inc."
        };

        await writeEventStore.AppendToStreamAsync(streamId, [tradeCaptured, tradeAmended, tradeBooked]);

        var tradeEvents = await (await readEventStore.ReadStreamAsync(streamId)).Events
            .Select(x => x.Payload)
            .OfType<TradeEvent>()
            .ToArrayAsync();

        tradeEvents.Length.ShouldBe(3);

        tradeEvents[0].TradeId.ShouldBe(tradeId);
        tradeEvents[0].Amount.ShouldBe(10_000);
        tradeEvents[0].Currency.ShouldBe("EUR");

        tradeEvents[1].TradeId.ShouldBe(tradeId);
        tradeEvents[1].Amount.ShouldBe(15_000);
        tradeEvents[1].Currency.ShouldBe("CHF");

        tradeEvents[2].TradeId.ShouldBe(tradeId);
        tradeEvents[2].Amount.ShouldBe(15_000);
        tradeEvents[2].Currency.ShouldBe("CHF");
    }

    // ReSharper disable UnusedAutoPropertyAccessor.Global

    public record TradeCaptured
    {
        public required Guid TradeId { get; init; }
        public required decimal Amount { get; init; }
        public required string Currency { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required string CreatedBy { get; init; }
    }

    public record TradeAmended
    {
        public required Guid TradeId { get; init; }
        public required decimal Amount { get; init; }
        public required string Currency { get; init; }
        public required DateTimeOffset AmendedAt { get; init; }
        public required string AmendedBy { get; init; }
    }

    public record TradeBooked
    {
        public required Guid TradeId { get; init; }
        public required decimal Amount { get; init; }
        public required string Currency { get; init; }
        public required DateTimeOffset BookedAt { get; init; }
        public required string BookedBy { get; init; }
        public required string Counterparty { get; init; }
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    public record TradeEvent
    {
        public required Guid TradeId { get; init; }
        public required decimal Amount { get; init; }
        public required string Currency { get; init; }
    }

    // ReSharper restore UnusedAutoPropertyAccessor.Global
}