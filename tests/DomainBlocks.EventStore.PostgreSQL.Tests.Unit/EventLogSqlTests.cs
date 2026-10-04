using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class EventLogSqlTests
{
    private const string EventLog = "\"dbx_tests\".\"event_log\"";

    private const string Columns =
        "position, stream_id, stream_position, event_name, event_data, event_data_bytes, metadata, created_at";

    private static readonly EventLogSql Sql = new(new SchemaObjectNames("dbx_tests"));

    [Test]
    public void ReadAll_WithoutCondition_ReturnsQueryUnchanged()
    {
        Sql.ReadAll(ReadDirection.Forward, includeMetadata: true).ShouldBeSameAs(Sql.ReadAllForward);
        Sql.ReadAll(ReadDirection.Backward, includeMetadata: true).ShouldBeSameAs(Sql.ReadAllBackward);
    }

    [Test]
    public void ReadStream_WithoutCondition_ReturnsQueryUnchanged()
    {
        Sql.ReadStream(ReadDirection.Forward, includeMetadata: true).ShouldBeSameAs(Sql.ReadStreamForward);
        Sql.ReadStream(ReadDirection.Backward, includeMetadata: true).ShouldBeSameAs(Sql.ReadStreamBackward);
    }

    [Test]
    public void ReadAll_WithCondition_AddsConditionBeforeOrderBy()
    {
        var forward = Sql.ReadAll(ReadDirection.Forward, includeMetadata: true, "event_name = ANY($3)");
        var backward = Sql.ReadAll(ReadDirection.Backward, includeMetadata: true, "event_name = ANY($3)");

        forward.ShouldBe(
            $"SELECT {Columns} FROM {EventLog} " +
            "WHERE position > $1 AND event_name = ANY($3) ORDER BY position LIMIT $2");

        backward.ShouldBe(
            $"SELECT {Columns} FROM {EventLog} " +
            "WHERE position < $1 AND event_name = ANY($3) ORDER BY position DESC LIMIT $2");
    }

    [Test]
    public void ReadStream_WithCondition_AddsConditionBeforeOrderBy()
    {
        var forward = Sql.ReadStream(ReadDirection.Forward, includeMetadata: true, "created_at < $4");
        var backward = Sql.ReadStream(ReadDirection.Backward, includeMetadata: true, "created_at < $4");

        forward.ShouldBe(
            $"SELECT {Columns} FROM {EventLog} " +
            "WHERE stream_id = $1 AND stream_position > $2 AND created_at < $4 ORDER BY stream_position LIMIT $3");

        backward.ShouldBe(
            $"SELECT {Columns} FROM {EventLog} " +
            "WHERE stream_id = $1 AND stream_position < $2 AND created_at < $4 ORDER BY stream_position DESC LIMIT $3");
    }

    [Test]
    public void ReadAll_WithConditionButWithoutMetadata_StillLeavesMetadataOutOfSelectedColumns()
    {
        var query = Sql.ReadAll(ReadDirection.Forward, includeMetadata: false, "(metadata ? $3) IS TRUE");

        query.ShouldStartWith("SELECT position, stream_id, stream_position, event_name, event_data, " +
                              "event_data_bytes, NULL::jsonb, created_at FROM");

        query.ShouldEndWith("WHERE position > $1 AND (metadata ? $3) IS TRUE ORDER BY position LIMIT $2");
    }

    [Test]
    public void ParameterCounts_ForPages_MatchPlaceholdersInQueries()
    {
        Sql.ReadAllForward.ShouldContain($"${EventLogSql.ReadAllParameterCount}");
        Sql.ReadAllForward.ShouldNotContain($"${EventLogSql.ReadAllParameterCount + 1}");
        Sql.ReadStreamForward.ShouldContain($"${EventLogSql.ReadStreamParameterCount}");
        Sql.ReadStreamForward.ShouldNotContain($"${EventLogSql.ReadStreamParameterCount + 1}");
    }
}