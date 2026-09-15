using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

[TestFixture]
public class MongoEventStoreSubscriptionTests() :
    EventStoreSubscriptionTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness());