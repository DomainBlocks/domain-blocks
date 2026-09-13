using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration.Contract;

[TestFixture]
public class KurrentDBEventStoreTests() :
    EventStoreTests<StreamPosition, Position>(new KurrentDBEventStoreHarness());