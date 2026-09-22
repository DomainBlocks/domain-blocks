using DomainBlocks.Testing.Integration.EventStore;
using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

/// <summary>
/// With payloads kept as documents, which the database can see into.
/// </summary>
[TestFixture]
public class MongoEventStoreLoweredPredicateTests() :
    EventStoreLoweredPredicateTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness())
{
    protected override EventFormat? Format => EventFormat.Bson;
}