using DomainBlocks.Benchmarking;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Benchmarks;

[TestFixture]
public class MongoEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness());