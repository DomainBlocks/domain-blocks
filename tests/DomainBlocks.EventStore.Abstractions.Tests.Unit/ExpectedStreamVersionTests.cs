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
    public void Kind_WhenInstanceIsAny_IsAny()
    {
        ExpectedStreamState.Any.Kind.ShouldBe(ExpectedStreamStateKind.Any);
    }

    [Test]
    public void Kind_WhenInstanceIsStreamExists_IsStreamExists()
    {
        ExpectedStreamState.StreamExists.Kind.ShouldBe(ExpectedStreamStateKind.StreamExists);
    }

    [Test]
    public void Kind_WhenInstanceIsStreamDoesNotExist_IsStreamDoesNotExist()
    {
        ExpectedStreamState.StreamDoesNotExist.Kind.ShouldBe(ExpectedStreamStateKind.StreamDoesNotExist);
    }

    [Test]
    public void FromVersion_WithNone_ReturnsStreamDoesNotExist()
    {
        var result = ExpectedStreamState.FromVersion(StreamVersion.None);
        result.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
    }

    [Test]
    public void FromVersion_WithSpecificVersion_ReturnsSpecificVersion()
    {
        var version = StreamVersion.FromInt64(42);
        var result = ExpectedStreamState.FromVersion(version);
        result.Kind.ShouldBe(ExpectedStreamStateKind.SpecificVersion);
    }

    [Test]
    public void Version_WhenAnyOrStreamExists_IsNull()
    {
        ExpectedStreamState.Any.Version.ShouldBeNull();
        ExpectedStreamState.StreamExists.Version.ShouldBeNull();
    }

    [Test]
    public void Version_WhenStreamDoesNotExist_IsNone()
    {
        ExpectedStreamState.StreamDoesNotExist.Version.ShouldBe(StreamVersion.None);
    }

    [Test]
    public void Version_WhenSpecificVersion_IsSpecificVersion()
    {
        var version = StreamVersion.FromInt64(123);
        var expected = ExpectedStreamState.FromVersion(version);
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
        var specific = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(99));
        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1));
        var b = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(1));
        var b = ExpectedStreamState.FromVersion(StreamVersion.FromInt64(2));

        a.Equals(b).ShouldBeFalse();
    }
}