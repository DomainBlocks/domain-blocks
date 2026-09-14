using DomainBlocks.Benchmarking;
using DomainBlocks.EventStore.Abstractions;
using NUnit.Framework;

namespace DomainBlocks.EventStore.NoOp.Benchmarks;

/// <summary>
/// Runs the shared append benchmarks against <see cref="NoOpEventStore"/> to measure the harness's own ceiling. Compare
/// a real store's figures against these: the closer they are, the more the harness rather than the store is being
/// measured. Needs no container, so it can run anywhere.
/// </summary>
[TestFixture]
public class NoOpEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new NoOpEventStoreTestHarness());