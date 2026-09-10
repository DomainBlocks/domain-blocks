using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.Benchmarking;
using NUnit.Framework;

namespace DomainBlocks.EventStore.Tests.Unit;

/// <summary>
/// Runs the shared append benchmarks against <see cref="NoOpEventStore"/> to measure the harness's own ceiling. Compare
/// a real store's figures against these: the closer they are, the more the harness rather than the store is being
/// measured. Needs no container, so it can run anywhere.
/// </summary>
[TestFixture]
public class NoOpEventStoreBenchmarkTests : EventStoreBenchmarkTests<StreamPosition, LogPosition>
{
    protected override Task<string?> DescribeStoreAsync() =>
        Task.FromResult<string?>("NoOpEventStore (harness ceiling), appends complete after a thread-pool hop");

    protected override IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        return new NoOpEventStore();
    }
}
