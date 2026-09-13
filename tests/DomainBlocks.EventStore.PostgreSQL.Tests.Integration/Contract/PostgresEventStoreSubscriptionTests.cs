using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreSubscriptionTests() :
    EventStoreSubscriptionTests<StreamPosition, LogPosition>(new PostgresEventStoreHarness());