using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamVersionTests
{
    [Test]
    public void Zero_ShouldHaveValueZero()
    {
        StreamVersion.Zero.ToUInt64().ShouldBe(0UL);
        StreamVersion.Zero.ToInt64().ShouldBe(0L);
    }

    [Test]
    public void FromInt64_ShouldSetValue()
    {
        var version = StreamVersion.FromInt64(123L);
        version.ToInt64().ShouldBe(123L);
    }

    [Test]
    public void FromUInt64_ShouldSetValue()
    {
        var version = StreamVersion.FromUInt64(42UL);
        version.ToUInt64().ShouldBe(42UL);
    }

    [Test]
    public void FromInt64_ShouldThrowForNegativeValue()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StreamVersion.FromInt64(-1L));
    }

    [Test]
    public void ToString_ShouldReturnValueAsString()
    {
        var version = StreamVersion.FromInt64(99);
        version.ToString().ShouldBe("99");
    }

    [Test]
    public void Equals_ShouldReturnTrueForSameValue()
    {
        var a = StreamVersion.FromInt64(7);
        var b = StreamVersion.FromInt64(7);
        a.ShouldBe(b);
        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_ShouldReturnFalseForDifferentValue()
    {
        var a = StreamVersion.FromInt64(7);
        var b = StreamVersion.FromInt64(8);
        a.ShouldNotBe(b);
        a.Equals(b).ShouldBeFalse();
    }

    [Test]
    public void CompareTo_ShouldOrderCorrectly()
    {
        var a = StreamVersion.FromInt64(1);
        var b = StreamVersion.FromInt64(2);
        a.CompareTo(b).ShouldBeLessThan(0);
        b.CompareTo(a).ShouldBeGreaterThan(0);
        a.CompareTo(a).ShouldBe(0);
    }

    [Test]
    public void CompareTo_Object_ShouldWork()
    {
        var a = StreamVersion.FromInt64(5);
        object b = StreamVersion.FromInt64(6);
        a.CompareTo(b).ShouldBeLessThan(0);
        a.CompareTo(null).ShouldBe(1);
        Should.Throw<ArgumentException>(() => a.CompareTo("not a StreamVersion"));
    }

    [Test]
    public void Operators_ShouldWorkAsExpected()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);

        (a < b).ShouldBeTrue();
        (a <= b).ShouldBeTrue();
        (b > a).ShouldBeTrue();
        (b >= a).ShouldBeTrue();
        // ReSharper disable once EqualExpressionComparison
        (a == a).ShouldBeTrue();
        (a != b).ShouldBeTrue();
    }

    [Test]
    public void GetHashCode_ShouldBeConsistent()
    {
        var a = StreamVersion.FromInt64(123);
        var b = StreamVersion.FromInt64(123);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}