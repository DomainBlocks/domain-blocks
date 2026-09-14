using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreEventRepresentationTests() :
    EventStoreEventRepresentationTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness());