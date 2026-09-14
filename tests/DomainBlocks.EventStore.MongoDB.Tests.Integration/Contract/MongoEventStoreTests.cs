using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration.Contract;

[TestFixture]
public class MongoEventStoreTests() :
    EventStoreTests<StreamPosition, LogPosition>(new MongoEventStoreTestHarness());