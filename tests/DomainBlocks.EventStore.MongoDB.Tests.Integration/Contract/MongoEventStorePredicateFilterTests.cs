using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

[TestFixture]
public class MongoEventStorePredicateFilterTests() :
    EventStorePredicateFilterTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness());