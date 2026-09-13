using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

[TestFixture]
public class MongoEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, LogPosition>(new MongoEventStoreHarness());