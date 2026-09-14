using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.KurrentDB;
using DomainBlocks.EventStore.TypeMapping;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.Testing.Integration.KurrentDB;

/// <summary>
/// Binds a suite to the KurrentDB event store on the server started by <see cref="KurrentDBTestEnvironment"/>.
/// KurrentDB has no per-fixture database, so initialising, resetting and dropping do nothing: the suites isolate
/// their tests with unique stream ids, and the store implements neither reads across the log nor subscriptions,
/// which are the operations that would assume an empty log.
/// </summary>
public sealed class KurrentDBEventStoreTestHarness : IEventStoreTestHarness<StreamPosition, Position>
{
    // The store passes the commit id through to nothing, so repeated appends are not deduplicated.
    public StoreCapabilities Capabilities => StoreCapabilities.None;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } = [EventFormat.Json];

    public Task InitializeAsync(string name) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;

    public Task DropAsync() => Task.CompletedTask;

    public IEventStore<object, string, StreamPosition, Position> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        var eventCodec = TestKurrentDBEventCodec.Create(eventTypeMap, eventFormat, contractMappers);
        return new KurrentDBEventStore<object>(KurrentDBTestEnvironment.Client, eventCodec);
    }

    public StreamPosition CreateStreamPosition(ulong value) => StreamPosition.FromStreamRevision(value);

    public Position CreateLogPosition(ulong value) => new(value, value);

    public Task<string?> DescribeAsync() =>
        Task.FromResult<string?>($"KurrentDBEventStore: image {KurrentDBTestEnvironment.Image}");
}