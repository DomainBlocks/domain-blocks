using DomainBlocks.Benchmarking.EventStore;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Benchmarks;

[TestFixture]
public class MongoEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness());