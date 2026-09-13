using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreTests() :
    EventStoreTests<StreamPosition, LogPosition>(new PostgresEventStoreHarness());