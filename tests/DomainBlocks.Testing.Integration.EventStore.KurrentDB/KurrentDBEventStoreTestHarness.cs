using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.TypeMapping;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.Testing.Integration.EventStore.KurrentDB;

public sealed class KurrentDBEventStoreTestHarness : IEventStoreTestHarness<StreamPosition, Position>
{
    public StoreCapabilities Capabilities => StoreCapabilities.None;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } = [EventFormat.Json];

    public Task InitializeAsync(string name) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;

    public Task DropAsync() => Task.CompletedTask;

    public IEventStore<object, string, StreamPosition, Position> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "",
        IEnumerable<string>? ignoredEventNames = null)
    {
        if (eventFormat is not null and not EventFormat.Json)
            throw new NotSupportedException($"{eventFormat} is not supported by the KurrentDB test harness.");

        var builder = new KurrentDBEventStoreBuilder<object>()
            .UseClient(KurrentDBTestEnvironment.Client)
            .ConfigureCodec(x => x
                .UseEventTypeMap(eventTypeMap)
                .AddContractMappers([.. contractMappers ?? []]));

        if (ignoredEventNames is not null)
            builder.IgnoreEvents([.. ignoredEventNames]).UseIgnoredEventSentinel(IgnoredEvent.Instance);

        return builder.Build();
    }

    public StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);

    public Position CreateLogPosition(ulong value) => new(value, value);

    public Task<string?> DescribeAsync() =>
        Task.FromResult<string?>($"KurrentDBEventStore: image {KurrentDBTestEnvironment.Image}");
}