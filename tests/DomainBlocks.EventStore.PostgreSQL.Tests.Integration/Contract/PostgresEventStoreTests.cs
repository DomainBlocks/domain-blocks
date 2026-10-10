using DomainBlocks.Testing.Integration.EventStore.Contract;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

/// <summary>
/// Runs with a small read page size so that the shared read tests exercise keyset paging.
/// </summary>
[TestFixture]
public class PostgresEventStoreTests() :
    EventStoreTests<StreamPosition, LogPosition>(new PostgresEventStoreTestHarness(x => x.ReadPageSize = 7));