using DomainBlocks.Benchmarking.EventStore;
using DomainBlocks.Testing.Integration.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Benchmarks;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;


[TestFixture]
public class KurrentDBEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());