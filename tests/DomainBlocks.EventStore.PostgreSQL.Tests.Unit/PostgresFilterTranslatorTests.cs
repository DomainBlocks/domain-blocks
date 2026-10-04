using DomainBlocks.EventStore.Filtering;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresFilterTranslatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Translate_All_IsTrue()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.All, 1);

        condition.Sql.ShouldBe("TRUE");
        condition.ParameterValues.ShouldBeEmpty();
    }

    [Test]
    public void Translate_None_IsFalse()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.None, 1);

        condition.Sql.ShouldBe("FALSE");
        condition.ParameterValues.ShouldBeEmpty();
    }

    [Test]
    public void Translate_EventNames_ComparesNameWithArrayParameter()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.EventNames("OrderShipped", "OrderPlaced"), 1);

        condition.Sql.ShouldBe("event_name = ANY($1)");
        condition.ParameterValues.ShouldHaveSingleItem().ShouldBe(new[] { "OrderPlaced", "OrderShipped" });
    }

    [Test]
    public void Translate_StreamIds_ComparesStreamIdWithArrayParameter()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.StreamIds("order-2", "order-1"), 1);

        condition.Sql.ShouldBe("stream_id = ANY($1)");
        condition.ParameterValues.ShouldHaveSingleItem().ShouldBe(new[] { "order-1", "order-2" });
    }

    [Test]
    public void Translate_StreamIdStartsWith_PassesPrefixUnescaped()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.StreamIdStartsWith("order_%'"), 1);

        condition.Sql.ShouldBe("starts_with(stream_id, $1)");
        condition.ParameterValues.ShouldBe(["order_%'"]);
    }

    [Test]
    public void Translate_MetadataExists_IsNeverNull()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.MetadataExists("tenant"), 1);

        condition.Sql.ShouldBe("(metadata ? $1) IS TRUE");
        condition.ParameterValues.ShouldBe(["tenant"]);
    }

    [Test]
    public void Translate_Metadata_ComparesValueWithArrayParameterAndIsNeverNull()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.Metadata("tenant", "initech", "acme"), 1);

        condition.Sql.ShouldBe("(metadata ->> $1 = ANY($2)) IS TRUE");
        condition.ParameterValues.Length.ShouldBe(2);
        condition.ParameterValues[0].ShouldBe("tenant");
        condition.ParameterValues[1].ShouldBe(new[] { "acme", "initech" });
    }

    [Test]
    public void Translate_CreatedAtOrAfter_ComparesWithLowerBound()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(Noon), 1);

        condition.Sql.ShouldBe("created_at >= $1");
        condition.ParameterValues.ShouldBe([Noon]);
    }

    [Test]
    public void Translate_CreatedBefore_ComparesWithUpperBound()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.CreatedBefore(Noon), 1);

        condition.Sql.ShouldBe("created_at < $1");
        condition.ParameterValues.ShouldBe([Noon]);
    }

    [TestCase(0, 0)]
    [TestCase(1, 10)]
    [TestCase(9, 10)]
    [TestCase(10, 10)]
    [TestCase(11, 20)]
    public void Translate_CreatedAtBoundBetweenMicroseconds_RoundsBoundUp(long ticks, long expectedTicks)
    {
        var from = PostgresFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(Noon.AddTicks(ticks)), 1);
        var before = PostgresFilterTranslator.Translate(EventFilter.CreatedBefore(Noon.AddTicks(ticks)), 1);

        from.ParameterValues.ShouldBe([Noon.AddTicks(expectedTicks)]);
        before.ParameterValues.ShouldBe([Noon.AddTicks(expectedTicks)]);
    }

    [Test]
    public void Translate_CreatedAtBoundWithOffset_ConvertsBoundToUtc()
    {
        var bound = Noon.ToOffset(TimeSpan.FromHours(5));

        var condition = PostgresFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(bound), 1);

        var value = condition.ParameterValues.ShouldHaveSingleItem().ShouldBeOfType<DateTimeOffset>();
        value.ShouldBe(Noon);
        value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Test]
    public void Translate_CreatedAtBoundAtLatestInstant_RoundsBoundDown()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.CreatedBefore(DateTimeOffset.MaxValue), 1);

        condition.ParameterValues.ShouldBe([DateTimeOffset.MaxValue.AddTicks(-9)]);
    }

    [Test]
    public void Translate_And_JoinsOperandsInOrderAndNumbersParametersInOrder()
    {
        var filter =
            EventFilter.EventNames("OrderPlaced") &
            EventFilter.Metadata("tenant", "acme") &
            EventFilter.CreatedBefore(Noon);

        var condition = PostgresFilterTranslator.Translate(filter, 1);

        condition.Sql.ShouldBe("(event_name = ANY($1) AND (metadata ->> $2 = ANY($3)) IS TRUE AND created_at < $4)");
        condition.ParameterValues.Length.ShouldBe(4);
        condition.ParameterValues[1].ShouldBe("tenant");
        condition.ParameterValues[3].ShouldBe(Noon);
    }

    [Test]
    public void Translate_Or_JoinsOperandsInOrder()
    {
        var filter = EventFilter.StreamIds("order-1") | EventFilter.MetadataExists("tenant");

        var condition = PostgresFilterTranslator.Translate(filter, 1);

        condition.Sql.ShouldBe("(stream_id = ANY($1) OR (metadata ? $2) IS TRUE)");
    }

    [Test]
    public void Translate_Not_NegatesOperandInParentheses()
    {
        var condition = PostgresFilterTranslator.Translate(!EventFilter.MetadataExists("tenant"), 1);

        condition.Sql.ShouldBe("(NOT (metadata ? $1) IS TRUE)");
    }

    [Test]
    public void Translate_NestedFilters_KeepsGroupingWithParentheses()
    {
        var filter =
            (EventFilter.EventNames("OrderPlaced") | EventFilter.StreamIdStartsWith("invoice-")) &
            !(EventFilter.MetadataExists("tenant") & EventFilter.CreatedAtOrAfter(Noon));

        var condition = PostgresFilterTranslator.Translate(filter, 1);

        condition.Sql.ShouldBe(
            "((event_name = ANY($1) OR starts_with(stream_id, $2)) AND " +
            "(NOT ((metadata ? $3) IS TRUE AND created_at >= $4)))");
    }

    [Test]
    public void Translate_FirstParameterIndex_NumbersParametersFromIt()
    {
        var filter = EventFilter.StreamIds("order-1") & EventFilter.Metadata("tenant", "acme");

        var condition = PostgresFilterTranslator.Translate(filter, 4);

        condition.Sql.ShouldBe("(stream_id = ANY($4) AND (metadata ->> $5 = ANY($6)) IS TRUE)");
    }

    [Test]
    public void Translate_SameShapeWithOtherValues_GivesSameSql()
    {
        var first = PostgresFilterTranslator.Translate(
            EventFilter.EventNames("OrderPlaced") & EventFilter.Metadata("tenant", "acme"),
            1);

        var second = PostgresFilterTranslator.Translate(
            EventFilter.EventNames("OrderShipped", "InvoiceRaised") & EventFilter.Metadata("region", "eu", "us"),
            1);

        second.Sql.ShouldBe(first.Sql);
    }

    [Test]
    public void Translate_FirstParameterIndexBelowOne_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            PostgresFilterTranslator.Translate(EventFilter.StreamIds("order-1"), 0));
    }

    [Test]
    public void AddParametersTo_Command_AddsTypedParametersInOrder()
    {
        var filter =
            EventFilter.StreamIdStartsWith("order-") &
            EventFilter.EventNames("OrderPlaced") &
            EventFilter.CreatedBefore(Noon);

        var condition = PostgresFilterTranslator.Translate(filter, 1);
        using var command = new NpgsqlCommand();

        condition.AddParametersTo(command.Parameters);

        command.Parameters.Count.ShouldBe(3);
        command.Parameters[0].ShouldBeOfType<NpgsqlParameter<string>>().TypedValue.ShouldBe("order-");
        command.Parameters[1].ShouldBeOfType<NpgsqlParameter<string[]>>().TypedValue.ShouldBe(["OrderPlaced"]);
        command.Parameters[2].ShouldBeOfType<NpgsqlParameter<DateTimeOffset>>().TypedValue.ShouldBe(Noon);
    }

    [Test]
    public void AddParametersTo_TwoCommands_AddsNewParametersToEach()
    {
        var condition = PostgresFilterTranslator.Translate(EventFilter.StreamIds("order-1"), 1);
        using var first = new NpgsqlCommand();
        using var second = new NpgsqlCommand();

        condition.AddParametersTo(first.Parameters);
        condition.AddParametersTo(second.Parameters);

        second.Parameters[0].ShouldNotBeSameAs(first.Parameters[0]);
    }
}