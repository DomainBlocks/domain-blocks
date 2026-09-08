using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class SqlNamesTests
{
    [Test]
    public void ValidSchema_QuotesAllNames()
    {
        var names = new SqlNames("dbx_tests");

        names.Schema.ShouldBe("dbx_tests");
        names.QuotedSchema.ShouldBe("\"dbx_tests\"");
        names.EventLog.ShouldBe("\"dbx_tests\".\"event_log\"");
        names.Sequences.ShouldBe("\"dbx_tests\".\"sequences\"");
        names.AppendEventsFunction.ShouldBe("\"dbx_tests\".\"append_events\"");
        names.PublicationName.ShouldBe("dbx_tests_event_log_pub");
    }

    [TestCase("")]
    [TestCase("Dbx")]
    [TestCase("1dbx")]
    [TestCase("dbx-tests")]
    [TestCase("dbx\"; drop schema public")]
    [TestCase("a234567890123456789012345678901234567890123456789012345678901234")]
    public void InvalidSchema_Throws(string schema)
    {
        Should.Throw<ArgumentException>(() => new SqlNames(schema));
    }

    [Test]
    public void NullSchema_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new SqlNames(null!));
    }
}
