using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration.Contract;

[TestFixture]
public class KurrentDBEventStoreConcurrencyTests() :
    EventStoreConcurrencyTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());