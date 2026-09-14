using DomainBlocks.Benchmarking;
using DomainBlocks.Testing.Integration.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Benchmarks;

[TestFixture]
public class KurrentDBEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());