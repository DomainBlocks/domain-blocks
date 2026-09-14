using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreConcurrencyTests() :
    EventStoreConcurrencyTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness());