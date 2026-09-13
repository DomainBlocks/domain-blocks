using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreBenchmarkTests() :
    EventStoreBenchmarkTests<StreamPosition, Position>(new KurrentDBEventStoreHarness());