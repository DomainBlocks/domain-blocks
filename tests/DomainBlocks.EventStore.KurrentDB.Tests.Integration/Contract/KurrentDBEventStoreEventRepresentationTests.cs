using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration.Contract;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;

[TestFixture]
public class KurrentDBEventStoreEventRepresentationTests() :
    EventStoreEventRepresentationTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());