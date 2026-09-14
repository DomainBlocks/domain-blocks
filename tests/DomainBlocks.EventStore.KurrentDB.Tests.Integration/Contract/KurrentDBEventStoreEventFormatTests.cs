using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration.Contract;

[TestFixture]
public class KurrentDBEventStoreEventFormatTests() :
    EventStoreEventFormatTests<StreamPosition, Position>(new KurrentDBEventStoreTestHarness());