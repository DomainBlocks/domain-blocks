using System.Text.RegularExpressions;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

// NamePattern is a PostgreSQL regular expression, which .NET reads the same way for the syntax it uses.
public class SlotNameGeneratorTests
{
    [Test]
    public void Next_Schema_NamesSlotAfterSchema()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames("orders"));

        generator.Next().ShouldMatch("^orders_[0-9a-f]{12}$");
    }

    [Test]
    public void Next_CalledTwice_ReturnsDifferentNames()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames("orders"));

        generator.Next().ShouldNotBe(generator.Next());
    }

    [Test]
    public void NamePattern_SlotOfSchema_Matches()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames("orders"));

        Regex.IsMatch(generator.Next(), generator.NamePattern).ShouldBeTrue();
    }

    [Test]
    public void NamePattern_SlotOfSchemaStartingWithSameName_DoesNotMatch()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames("orders"));
        var otherSlotName = new SlotNameGenerator(new SchemaObjectNames("orders_archive")).Next();

        Regex.IsMatch(otherSlotName, generator.NamePattern).ShouldBeFalse();
    }

    [Test]
    public void Next_SchemaOf50Characters_FitsSlotNameLimit()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames(new string('s', 50)));

        generator.Next().Length.ShouldBe(63);
    }

    [Test]
    public void Next_SchemaOf51Characters_Throws()
    {
        var generator = new SlotNameGenerator(new SchemaObjectNames(new string('s', 51)));

        Should.Throw<InvalidOperationException>(generator.Next);
    }
}