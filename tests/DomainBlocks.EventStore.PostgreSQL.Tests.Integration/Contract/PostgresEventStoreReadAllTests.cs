using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

/// <summary>
/// Runs with small read batches so that the shared tests exercise keyset paging.
/// </summary>
[TestFixture]
public class PostgresEventStoreReadAllTests() :
    EventStoreReadAllTests<StreamPosition, LogPosition>(new PostgresEventStoreHarness(x => x.ReadBatchSize = 7));