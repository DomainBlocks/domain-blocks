using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class ExpectedStreamVersionTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsAny()
    {
        default(ExpectedStreamVersion).ShouldBe(ExpectedStreamVersion.Any);
    }

    [Test]
    public void Kind_WhenInstanceIsAny_ReturnsAny()
    {
        ExpectedStreamVersion.Any.Kind.ShouldBe(ExpectedStreamVersionKind.Any);
    }

    [Test]
    public void Kind_WhenInstanceIsExists_ReturnsExits()
    {
        ExpectedStreamVersion.Exists.Kind.ShouldBe(ExpectedStreamVersionKind.Exists);
    }

    [Test]
    public void Kind_WhenInstanceIsNone_ReturnsNone()
    {
        ExpectedStreamVersion.None.Kind.ShouldBe(ExpectedStreamVersionKind.None);
    }

    [Test]
    public void FromVersion_WithNone_ReturnsNone()
    {
        var result = ExpectedStreamVersion.FromVersion(StreamVersion.None);
        result.ShouldBe(ExpectedStreamVersion.None);
    }

    [Test]
    public void FromVersion_WithSpecificVersion_ReturnsSpecific()
    {
        var version = StreamVersion.FromInt64(42);
        var result = ExpectedStreamVersion.FromVersion(version);
        result.Kind.ShouldBe(ExpectedStreamVersionKind.Specific);
    }

    [Test]
    public void ToVersion_WhenAnyOrExists_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => ExpectedStreamVersion.Any.ToVersion());
        Should.Throw<InvalidOperationException>(() => ExpectedStreamVersion.Exists.ToVersion());
    }

    [Test]
    public void ToVersion_WhenNone_ReturnsNone()
    {
        var result = ExpectedStreamVersion.None.ToVersion();
        result.ShouldBe(StreamVersion.None);
    }

    [Test]
    public void ToVersion_WhenSpecificVersion_ReturnsCorrectVersion()
    {
        var version = StreamVersion.FromInt64(123);
        var expected = ExpectedStreamVersion.FromVersion(version);
        expected.ToVersion().ShouldBe(version);
    }

    [Test]
    public void ToString_ForNonSpecificKinds_ReturnsName()
    {
        ExpectedStreamVersion.None.ToString().ShouldBe("None");
        ExpectedStreamVersion.Exists.ToString().ShouldBe("Exists");
        ExpectedStreamVersion.Any.ToString().ShouldBe("Any");
    }

    [Test]
    public void ToString_ForSpecificKind_ReturnsNumericValue()
    {
        var specific = ExpectedStreamVersion.FromVersion(StreamVersion.FromInt64(99));
        specific.ToString().ShouldBe("99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = ExpectedStreamVersion.FromVersion(StreamVersion.FromInt64(1));
        var b = ExpectedStreamVersion.FromVersion(StreamVersion.FromInt64(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = ExpectedStreamVersion.FromVersion(StreamVersion.FromInt64(1));
        var b = ExpectedStreamVersion.FromVersion(StreamVersion.FromInt64(2));

        a.Equals(b).ShouldBeFalse();
    }
}