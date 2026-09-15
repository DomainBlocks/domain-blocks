using DomainBlocks.EventStore;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration.EventStore;

namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Binds the shared benchmarks to <see cref="NoOpEventStore"/>. There is no store to initialise or reset.
/// </summary>
public sealed class NoOpEventStoreTestHarness : IEventStoreTestHarness<StreamPosition, LogPosition>
{
    public StoreCapabilities Capabilities => StoreCapabilities.None;

    public IReadOnlyList<EventFormat> SupportedFormats { get; } = [];

    public Task InitializeAsync(string name) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;

    public Task DropAsync() => Task.CompletedTask;

    public IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        return new NoOpEventStore();
    }

    public StreamPosition CreateStreamPosition(ulong value) => new(value);

    public LogPosition CreateLogPosition(ulong value) => new(value);

    public Task<string?> DescribeAsync() =>
        Task.FromResult<string?>("NoOpEventStore (harness ceiling), appends complete after a thread-pool hop");
}