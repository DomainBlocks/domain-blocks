using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class EventLogSqlTests
{
    private static readonly EventLogSql Sql = new(new SchemaObjectNames("s"));

    [Test]
    public void WithCondition_WhenGivenAQuery_AddsTheConditionBeforeTheOrdering()
    {
        EventLogSql.WithCondition(Sql.ReadAllForward, "event_name = ANY($3)").ShouldBe(
            Sql.ReadAllForward.Replace(" ORDER BY ", " AND event_name = ANY($3) ORDER BY "));
    }

    [Test]
    public void WithCondition_WhenGivenAnyPagedQuery_KeepsItsOneOrdering()
    {
        string[] queries =
        [
            Sql.ReadAllForward, Sql.ReadAllBackward, Sql.ReadAllForwardWithoutMetadata, Sql.ReadStreamForward,
            Sql.ReadStreamBackward, Sql.ReadStreamBackwardWithoutMetadata, Sql.ReadCatchUpAll, Sql.ReadCatchUpStream
        ];

        foreach (var query in queries)
        {
            var filtered = EventLogSql.WithCondition(query, "(TRUE)");

            filtered.ShouldContain(" AND (TRUE) ORDER BY ");
            filtered.Replace(" AND (TRUE)", "").ShouldBe(query);
        }
    }

    [Test]
    public void WithCondition_WhenTheQueryHasNoOrdering_Throws()
    {
        Should.Throw<ArgumentException>(() => EventLogSql.WithCondition(Sql.MaxPosition, "TRUE"));
    }
}