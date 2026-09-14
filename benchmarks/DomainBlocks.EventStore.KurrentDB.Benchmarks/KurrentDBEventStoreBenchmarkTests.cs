using DomainBlocks.Benchmarking.EventStore;
using DomainBlocks.Testing.Integration.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Benchmarks;

[TestFixture]
public class KurrentDBEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());