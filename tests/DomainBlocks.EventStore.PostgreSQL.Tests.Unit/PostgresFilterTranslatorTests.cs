using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresFilterTranslatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Translate_WhenAllOrNone_IsAConstant()
    {
        ShouldTranslate(EventFilter.All, "TRUE");
        ShouldTranslate(EventFilter.None, "FALSE");
    }

    [Test]
    public void Translate_WhenByNameOrStream_ComparesWithOneArray()
    {
        ShouldTranslate(EventFilter.EventNames("B", "A"), "event_name = ANY($1)", [new[] { "A", "B" }]);
        ShouldTranslate(EventFilter.StreamIds("order-1"), "stream_id = ANY($1)", [new[] { "order-1" }]);
    }

    [Test]
    public void Translate_WhenByPrefix_TakesThePrefixAsItIs()
    {
        ShouldTranslate(EventFilter.StreamIdStartsWith("order_%"), "starts_with(stream_id, $1)", ["order_%"]);
    }

    [Test]
    public void Translate_WhenByMetadata_IsNeverNull()
    {
        ShouldTranslate(EventFilter.MetadataExists("tenant"), "(metadata ? $1) IS TRUE", ["tenant"]);

        ShouldTranslate(
            EventFilter.Metadata("tenant", "b", "a"),
            "(metadata ->> $1 = ANY($2)) IS TRUE",
            ["tenant", new[] { "a", "b" }]);
    }

    [Test]
    public void Translate_WhenByCreationTime_BoundsTheColumn()
    {
        ShouldTranslate(EventFilter.CreatedAtOrAfter(Noon), "(created_at >= $1)", [Noon]);
        ShouldTranslate(EventFilter.CreatedBefore(Noon), "(created_at < $1)", [Noon]);

        ShouldTranslate(
            EventFilter.CreatedAtOrAfter(Noon) & EventFilter.CreatedBefore(Noon.AddHours(1)),
            "((created_at >= $1) AND (created_at < $2))",
            [Noon, Noon.AddHours(1)]);
    }

    [Test]
    public void Translate_WhenABoundIsFinerThanAMicrosecond_RoundsItUp()
    {
        // Between a microsecond and the next there is no event, so rounding up selects the same events.
        var bound = Noon.AddTicks(1);
        var nextMicrosecond = Noon.AddTicks(TimeSpan.TicksPerMicrosecond);

        Translate(EventFilter.CreatedAtOrAfter(bound)).Parameters.ShouldBe([nextMicrosecond]);
        Translate(EventFilter.CreatedBefore(bound)).Parameters.ShouldBe([nextMicrosecond]);
        Translate(EventFilter.CreatedBefore(nextMicrosecond)).Parameters.ShouldBe([nextMicrosecond]);
    }

    [Test]
    public void Translate_WhenABoundHasAnOffset_GivesTheInstantInUtc()
    {
        var bound = Translate(EventFilter.CreatedBefore(Noon.ToOffset(TimeSpan.FromHours(5))))
            .Parameters
            .ShouldHaveSingleItem();

        bound.ShouldBeOfType<DateTimeOffset>().Offset.ShouldBe(TimeSpan.Zero);
        bound.ShouldBe(Noon);
    }

    [Test]
    public void Translate_WhenCombined_ParenthesisesAndNumbersParametersInOrder()
    {
        var filter = EventFilter.EventNames("A") &
                     (EventFilter.StreamIdStartsWith("order-") | !EventFilter.Metadata("tenant", "acme"));

        ShouldTranslate(
            filter,
            "(event_name = ANY($1) AND (starts_with(stream_id, $2) OR NOT ((metadata ->> $3 = ANY($4)) IS TRUE)))",
            [new[] { "A" }, "order-", "tenant", new[] { "acme" }]);
    }

    [Test]
    public void Translate_WhenTheQueryHasParametersOfItsOwn_NumbersAfterThem()
    {
        var sql = PostgresFilterTranslator.Translate(EventFilter.StreamIds("s") & EventFilter.EventNames("A"), 4);

        sql.Text.ShouldBe("(stream_id = ANY($4) AND event_name = ANY($5))");
    }

    [Test]
    public void Translate_WhenFiltersDifferOnlyInValues_GivesTheSameText()
    {
        var one = EventFilter.EventNames("A") & EventFilter.Metadata("tenant", "acme");
        var other = EventFilter.EventNames("B", "C", "D") & EventFilter.Metadata("user", "x", "y");

        Translate(one).Text.ShouldBe(Translate(other).Text);
    }

    [Test]
    public void TranslateToLiteralSql_WhenGivenAFilter_WritesItsValuesIntoTheCondition()
    {
        var filter = EventFilter.EventNames("B", "A") &
                     EventFilter.StreamIdStartsWith("order-") &
                     !EventFilter.Metadata("tenant", "acme") &
                     EventFilter.CreatedAtOrAfter(Noon.AddTicks(1));

        PostgresFilterTranslator.TranslateToLiteralSql(filter).ShouldBe(
            "(event_name = ANY(ARRAY['A', 'B']::text[]) AND " +
            "starts_with(stream_id, 'order-') AND " +
            "(created_at >= '2026-01-01 12:00:00.000001+00'::timestamptz) AND " +
            "NOT ((metadata ->> 'tenant' = ANY(ARRAY['acme']::text[])) IS TRUE))");
    }

    [Test]
    public void TranslateToLiteralSql_WhenAValueHasAQuote_DoublesIt()
    {
        PostgresFilterTranslator.TranslateToLiteralSql(EventFilter.StreamIds("o'brien's \"x\""))
            .ShouldBe("""stream_id = ANY(ARRAY['o''brien''s "x"']::text[])""");
    }

    [Test]
    public void TranslateToLiteralSql_WhenAValueHasABackslash_WritesItSoThatEveryServerReadsItAlike()
    {
        // A server with standard_conforming_strings off takes a backslash in a plain string for an escape, and \' for
        // a quote, which would end the string early. An escape string means the same whatever the setting.
        PostgresFilterTranslator.TranslateToLiteralSql(EventFilter.StreamIds("""a\'b"""))
            .ShouldBe("""stream_id = ANY(ARRAY[E'a\\''b']::text[])""");
    }

    [Test]
    public void TranslateToLiteralSql_WhenAValueHasANul_Throws()
    {
        Should.Throw<ArgumentException>(
            () => PostgresFilterTranslator.TranslateToLiteralSql(EventFilter.StreamIds("a\0b")));
    }

    [Test]
    public void TranslateToLiteralSql_WhenGivenAnOffset_WritesTheInstantInUtc()
    {
        var bound = Noon.ToOffset(TimeSpan.FromHours(5));

        PostgresFilterTranslator.TranslateToLiteralSql(EventFilter.CreatedBefore(bound))
            .ShouldBe("(created_at < '2026-01-01 12:00:00.000000+00'::timestamptz)");
    }

    [Test]
    public void Translate_WhenABoundIsTheLastInstantThereIs_DoesNotRoundItUpPastIt()
    {
        // As a caller might say "with no end". There is no later whole microsecond to round up to.
        var sql = PostgresFilterTranslator.Translate(EventFilter.CreatedBefore(DateTimeOffset.MaxValue), 1);

        sql.ParameterValues.ShouldBe([DateTimeOffset.MaxValue.AddTicks(-9)]);
    }

    [Test]
    public void CanPush_WhenALeafIsAboutAColumn_IsTrue()
    {
        EventFilter[] leaves =
        [
            EventFilter.EventNames("A"),
            EventFilter.StreamIds("s"),
            EventFilter.StreamIdStartsWith("s"),
            EventFilter.MetadataExists("k"),
            EventFilter.Metadata("k", "v"),
            EventFilter.CreatedBefore(Noon)
        ];

        leaves.ShouldAllBe(x => PostgresFilterTranslator.CanPush(x));
    }

    [Test]
    public void CanPush_WhenALeafNeedsTheEvent_IsFalse()
    {
        PostgresFilterTranslator.CanPush(EventFilter.OfType<string>()).ShouldBeFalse();
        PostgresFilterTranslator.CanPush(EventFilter.OfType<string>(e => e.Length > 0)).ShouldBeFalse();
    }

    [Test]
    public void Translate_WhenGivenALeafItCannotPush_Throws()
    {
        Should.Throw<ArgumentException>(() => PostgresFilterTranslator.Translate(EventFilter.OfType<string>(), 1));
    }

    [Test]
    public void AddParametersTo_WhenCalled_AddsATypedParameterForEachValue()
    {
        var sql = PostgresFilterTranslator.Translate(
            EventFilter.EventNames("A") & EventFilter.StreamIdStartsWith("s") & EventFilter.CreatedBefore(Noon),
            1);

        using var command = new Npgsql.NpgsqlCommand();
        sql.AddParametersTo(command.Parameters);

        command.Parameters.Count.ShouldBe(3);
        command.Parameters[0].ShouldBeOfType<Npgsql.NpgsqlParameter<string[]>>().TypedValue.ShouldBe(["A"]);
        command.Parameters[1].ShouldBeOfType<Npgsql.NpgsqlParameter<string>>().TypedValue.ShouldBe("s");
        command.Parameters[2].ShouldBeOfType<Npgsql.NpgsqlParameter<DateTimeOffset>>().TypedValue.ShouldBe(Noon);
    }

    private static void ShouldTranslate(EventFilter filter, string text, object[]? parameters = null)
    {
        var (actualText, actualParameters) = Translate(filter);

        actualText.ShouldBe(text);
        actualParameters.Length.ShouldBe(parameters?.Length ?? 0);

        // An array is compared by what is in it.
        foreach (var (actual, expected) in actualParameters.Zip(parameters ?? []))
        {
            if (expected is string[] texts)
                actual.ShouldBeOfType<string[]>().ShouldBe(texts);
            else
                actual.ShouldBe(expected);
        }
    }

    private static (string Text, object[] Parameters) Translate(EventFilter filter)
    {
        var sql = PostgresFilterTranslator.Translate(filter, 1);

        return (sql.Text, [.. sql.ParameterValues]);
    }
}