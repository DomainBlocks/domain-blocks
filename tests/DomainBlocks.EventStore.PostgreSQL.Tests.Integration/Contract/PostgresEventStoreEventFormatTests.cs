using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreEventFormatTests() :
    EventStoreEventFormatTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness());