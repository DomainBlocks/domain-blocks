using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.TypeMapping;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.Testing.Integration.EventStore.KurrentDB;

/// <summary>
/// Binds a suite to the KurrentDB event store on the server started by <see cref="KurrentDBTestEnvironment"/>. Stores
/// are built on the borrowed-client path of <see cref="KurrentDBEventStoreBuilder{TEvent}"/>.
/// </summary>
public sealed class KurrentDBEventStoreTestHarness : IEventStoreTestHarness<StreamPosition, Position>
{
    public StoreCapabilities Capabilities => StoreCapabilities.None;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } = [EventFormat.Json];

    public Task InitializeAsync(string name) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;

    public Task DropAsync() => Task.CompletedTask;

    /// <summary>
    /// A builder over the shared client, for callers that need to configure more than <see cref="CreateEventStore"/>
    /// offers.
    /// </summary>
    public static KurrentDBEventStoreBuilder<object> CreateBuilder() =>
        new KurrentDBEventStoreBuilder<object>().UseClient(KurrentDBTestEnvironment.Client);

    public IEventStore<object, string, StreamPosition, Position> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        if (eventFormat is not null and not EventFormat.Json)
            throw new NotSupportedException($"{eventFormat} is not supported by the KurrentDB test harness.");

        return CreateBuilder()
            .UseEventTypeMap(eventTypeMap)
            .AddContractMappers([.. contractMappers ?? []])
            .Build();
    }

    public StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);

    public Position CreateLogPosition(ulong value) => new(value, value);

    public Task<string?> DescribeAsync() =>
        Task.FromResult<string?>($"KurrentDBEventStore: image {KurrentDBTestEnvironment.Image}");
}