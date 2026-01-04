using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class ExpectedStreamStateTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsAny()
    {
        default(ExpectedStreamState).ShouldBe(ExpectedStreamState.Any);
    }

    [Test]
    public void Version_WhenNonSpecificVersion_IsNull()
    {
        ExpectedStreamState.Any.Version.ShouldBeNull();
        ExpectedStreamState.StreamExists.Version.ShouldBeNull();
        ExpectedStreamState.StreamDoesNotExist.Version.ShouldBeNull();
    }

    [Test]
    public void Version_WhenSpecificVersion_IsSpecificVersion()
    {
        var version = new StreamVersion(42);
        var expected = ExpectedStreamState.SpecificVersion(version);
        expected.Version.ShouldBe(version);
    }

    [Test]
    public void ToString_WhenNonSpecificVersion_ReturnsName()
    {
        ExpectedStreamState.Any.ToString().ShouldBe("Any");
        ExpectedStreamState.StreamExists.ToString().ShouldBe("StreamExists");
        ExpectedStreamState.StreamDoesNotExist.ToString().ShouldBe("StreamDoesNotExist");
    }

    [Test]
    public void ToString_ForSpecificVersion_ReturnsNumericValue()
    {
        var specific = ExpectedStreamState.SpecificVersion(new StreamVersion(99));
        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = ExpectedStreamState.SpecificVersion(new StreamVersion(1));
        var b = ExpectedStreamState.SpecificVersion(new StreamVersion(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = ExpectedStreamState.SpecificVersion(new StreamVersion(1));
        var b = ExpectedStreamState.SpecificVersion(new StreamVersion(2));

        a.Equals(b).ShouldBeFalse();
        a.Equals(ExpectedStreamState.Any).ShouldBeFalse();
        a.Equals(ExpectedStreamState.StreamExists).ShouldBeFalse();
        a.Equals(ExpectedStreamState.StreamDoesNotExist).ShouldBeFalse();

        ExpectedStreamState.Any.Equals(ExpectedStreamState.StreamExists).ShouldBeFalse();
        ExpectedStreamState.Any.Equals(ExpectedStreamState.StreamDoesNotExist).ShouldBeFalse();
        ExpectedStreamState.StreamExists.Equals(ExpectedStreamState.StreamDoesNotExist).ShouldBeFalse();
    }
}