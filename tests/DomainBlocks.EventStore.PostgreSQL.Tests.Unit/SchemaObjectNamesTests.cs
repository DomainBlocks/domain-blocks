using DomainBlocks.EventStore.Filtering;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class SchemaObjectNamesTests
{
    [Test]
    public void ValidSchema_QuotesAllNames()
    {
        var names = new SchemaObjectNames("dbx_tests");

        names.Schema.ShouldBe("dbx_tests");
        names.QuotedSchema.ShouldBe("\"dbx_tests\"");
        names.EventLog.ShouldBe("\"dbx_tests\".\"event_log\"");
        names.AppendEventsFunction.ShouldBe("\"dbx_tests\".\"append_events\"");
        names.Publication.ShouldBe("dbx_tests_event_log_pub");
    }

    [TestCase("")]
    [TestCase("Dbx")]
    [TestCase("1dbx")]
    [TestCase("dbx-tests")]
    [TestCase("dbx\"; drop schema public")]
    [TestCase("a234567890123456789012345678901234567890123456789012345678901234")]
    public void InvalidSchema_Throws(string schema)
    {
        Should.Throw<ArgumentException>(() => new SchemaObjectNames(schema));
    }

    [Test]
    public void NullSchema_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SchemaObjectNames(null!));
    }

    [Test]
    public void Publication_WhenThereIsNoSubscriptionFilter_IsNamedAfterTheSchemaAlone()
    {
        new SchemaObjectNames("dbx_tests").Publication.ShouldBe("dbx_tests_event_log_pub");
        new SchemaObjectNames("dbx_tests", EventFilter.All).Publication.ShouldBe("dbx_tests_event_log_pub");
    }

    [Test]
    public void Publication_WhenThereIsASubscriptionFilter_IsNamedAfterItToo()
    {
        var filter = EventFilter.StreamIdStartsWith("order-") & EventFilter.EventNames("B", "A");
        var names = new SchemaObjectNames("dbx_tests", filter);

        names.PublicationPrefix.ShouldBe("dbx_tests_event_log_pub");
        names.Publication.ShouldMatch("^dbx_tests_event_log_pub_[0-9a-f]{8}$");

        // The same filter, built again, in any process.
        var again = EventFilter.StreamIdStartsWith("order-") & EventFilter.EventNames("A", "B");
        new SchemaObjectNames("dbx_tests", again).Publication.ShouldBe(names.Publication);

        new SchemaObjectNames("dbx_tests", EventFilter.StreamIdStartsWith("order_")).Publication
            .ShouldNotBe(names.Publication);
    }

    [Test]
    public void Publication_WhenTheFilterIsFixed_HasTheNameItAlwaysHad()
    {
        // Pinned, as a name that changed would leave a running store without its publication.
        new SchemaObjectNames("dbx", EventFilter.StreamId("order-1")).Publication
            .ShouldBe("dbx_event_log_pub_" + Sha256Prefix("""streamId("order-1")"""));
    }

    [Test]
    public void Constructor_WhenTheNameOfThePublicationWouldBeCutShort_Throws()
    {
        var filter = EventFilter.StreamId("s");

        Should.NotThrow(() => new SchemaObjectNames(new string('a', 40), filter));
        Should.Throw<ArgumentException>(() => new SchemaObjectNames(new string('a', 41), filter));

        // Without a filter the name is as it always was, and a store that only appends and reads never uses it.
        Should.NotThrow(() => new SchemaObjectNames(new string('a', 63)));
        Should.NotThrow(() => new SchemaObjectNames(new string('a', 63), EventFilter.All));
    }

    private static string Sha256Prefix(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash)[..8];
    }
}