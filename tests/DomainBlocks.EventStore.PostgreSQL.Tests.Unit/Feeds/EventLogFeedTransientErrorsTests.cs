using DomainBlocks.EventStore.PostgreSQL.Feeds;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

public class EventLogFeedTransientErrorsTests
{
    [Test]
    public void IsTransient_ObjectNotInPrerequisiteState_ReturnsFalse()
    {
        // What creating the slot gives on a server without wal_level = logical.
        var exception = new PostgresException(
            "logical decoding requires \"wal_level\" >= \"logical\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.ObjectNotInPrerequisiteState);

        EventLogFeedTransientErrors.IsTransient(exception).ShouldBeFalse();
    }
}