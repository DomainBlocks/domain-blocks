using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamPositionTests
{
    [Test]
    public void Start_ShouldHaveValueZero()
    {
        StreamPosition.Start.ToInt64().ShouldBe(0);
    }

    [Test]
    public void End_ShouldHaveValueInt64MaxValue()
    {
        StreamPosition.End.ToInt64().ShouldBe(long.MaxValue);
    }

    [Test]
    public void FromInt64_ShouldSetValue()
    {
        var version = StreamPosition.FromInt64(123L);
        version.ToInt64().ShouldBe(123L);
    }

    [Test]
    public void FromUInt64_ShouldSetValue()
    {
        var pos = StreamPosition.FromUInt64(42);
        pos.ToUInt64().ShouldBe(42UL);
    }

    [Test]
    public void FromInt64_ShouldThrowForNegativeValue()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StreamPosition.FromInt64(-1L));
    }

    [Test]
    public void At_ShouldSetValueFromStreamVersion()
    {
        var version = StreamVersion.FromInt64(10);
        var pos = StreamPosition.At(version);
        pos.ToInt64().ShouldBe(10);
    }

    [Test]
    public void Equals_ShouldReturnTrueForSameValue()
    {
        var a = StreamPosition.FromInt64(7);
        var b = StreamPosition.FromInt64(7);
        a.ShouldBe(b);
        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_ShouldReturnFalseForDifferentValue()
    {
        var a = StreamPosition.FromInt64(7);
        var b = StreamPosition.FromInt64(8);
        a.ShouldNotBe(b);
        a.Equals(b).ShouldBeFalse();
    }

    [Test]
    public void CompareTo_ShouldOrderCorrectly()
    {
        var a = StreamPosition.FromInt64(1);
        var b = StreamPosition.FromInt64(2);
        a.CompareTo(b).ShouldBeLessThan(0);
        b.CompareTo(a).ShouldBeGreaterThan(0);
        a.CompareTo(a).ShouldBe(0);
    }

    [Test]
    public void Operators_ShouldWorkAsExpected()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);

        (a < b).ShouldBeTrue();
        (a <= b).ShouldBeTrue();
        (b > a).ShouldBeTrue();
        (b >= a).ShouldBeTrue();
        // ReSharper disable once EqualExpressionComparison
        (a == a).ShouldBeTrue();
        (a != b).ShouldBeTrue();
    }

    [Test]
    public void ToString_ShouldReturnStartOrEndOrValue()
    {
        StreamPosition.Start.ToString().ShouldBe("Start");
        StreamPosition.End.ToString().ShouldBe("End");
        StreamPosition.FromUInt64(123).ToString().ShouldBe("123");
    }
}