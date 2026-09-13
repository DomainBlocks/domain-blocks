using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.Testing.Integration.Contract;
using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration.Contract;

[TestFixture]
public class PostgresEventStoreEventFormatTests() :
    EventStoreEventFormatTests<StreamPosition, LogPosition>(new PostgresEventStoreHarness());