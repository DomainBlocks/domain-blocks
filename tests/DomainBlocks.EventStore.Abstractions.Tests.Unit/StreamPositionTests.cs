using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamPositionTests
{
    [Test]
    public void At_WithStreamVersion_SetsValue()
    {
        var version = StreamVersion.FromInt64(10);
        var pos = StreamPosition.At(version);
        pos.ToInt64().ShouldBe(10);
    }

    [Test]
    public void FromInt64_WithValidValue_SetsValue()
    {
        var version = StreamPosition.FromInt64(123);
        version.ToInt64().ShouldBe(123);
    }

    [Test]
    public void FromUInt64_WithValidValue_SetsValue()
    {
        var pos = StreamPosition.FromUInt64(42);
        pos.ToUInt64().ShouldBe(42UL);
    }

    [Test]
    public void FromInt64_WithNegativeValue_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StreamPosition.FromInt64(-1L));
    }

    [Test]
    public void ToInt64_WhenInstanceIsStart_ReturnsZero()
    {
        StreamPosition.Start.ToInt64().ShouldBe(0);
    }

    [Test]
    public void ToInt64_WhenInstanceIsEnd_ReturnsMaxValue()
    {
        StreamPosition.End.ToInt64().ShouldBe(long.MaxValue);
    }

    [Test]
    public void ToString_WithStartOrEndOrValue_ReturnsExpectedResults()
    {
        StreamPosition.Start.ToString().ShouldBe("Start");
        StreamPosition.End.ToString().ShouldBe("End");
        StreamPosition.FromUInt64(123).ToString().ShouldBe("123");
    }

    [Test]
    public void Equals_WithSameOrDifferentValues_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(7);
        var b = StreamPosition.FromInt64(7);
        var c = StreamPosition.FromInt64(8);

        a.Equals(b).ShouldBeTrue();
        a.Equals(c).ShouldBeFalse();
    }

    [Test]
    public void GetHashCode_WithSameValue_ReturnsSameCode()
    {
        var a = StreamPosition.FromInt64(123);
        var b = StreamPosition.FromInt64(123);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Test]
    public void CompareTo_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(1);
        var b = StreamPosition.FromInt64(2);

        a.CompareTo(b).ShouldBeLessThan(0);
        b.CompareTo(a).ShouldBeGreaterThan(0);
        a.CompareTo(a).ShouldBe(0);
    }

    [Test]
    public void LessThan_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (a < b).ShouldBeTrue();
        (b < a).ShouldBeFalse();
        (a < c).ShouldBeFalse();
    }

    [Test]
    public void GreaterThan_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (b > a).ShouldBeTrue();
        (a > b).ShouldBeFalse();
        (a > c).ShouldBeFalse();
    }

    [Test]
    public void LessThanOrEqual_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (a <= b).ShouldBeTrue();
        (b <= a).ShouldBeFalse();
        (a <= c).ShouldBeTrue();
    }

    [Test]
    public void GreaterThanOrEqual_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (b >= a).ShouldBeTrue();
        (a >= b).ShouldBeFalse();
        (a >= c).ShouldBeTrue();
    }

    [Test]
    public void EqualityOperator_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (a == c).ShouldBeTrue();
        (a == b).ShouldBeFalse();
    }

    [Test]
    public void InequalityOperator_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamPosition.FromInt64(3);
        var b = StreamPosition.FromInt64(4);
        var c = StreamPosition.FromInt64(3);

        (a != b).ShouldBeTrue();
        (a != c).ShouldBeFalse();
    }
}