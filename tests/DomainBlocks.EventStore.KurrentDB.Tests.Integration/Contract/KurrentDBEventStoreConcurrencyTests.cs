using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration.Contract;

[TestFixture]
public class KurrentDBEventStoreConcurrencyTests() :
    EventStoreConcurrencyTests<StreamPosition, Position>(new KurrentDBEventStoreHarness());