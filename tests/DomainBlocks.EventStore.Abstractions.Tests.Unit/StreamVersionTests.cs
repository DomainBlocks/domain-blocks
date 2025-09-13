using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamVersionTests
{
    [Test]
    public void HasValue_WhenInstanceIsNone_ReturnsFalse()
    {
        StreamVersion.None.HasValue.ShouldBeFalse();
    }

    [Test]
    public void HasValue_WhenSpecificValue_ReturnsTrue()
    {
        StreamVersion.Zero.HasValue.ShouldBeTrue();
        StreamVersion.FromInt64(1).HasValue.ShouldBeTrue();
    }

    [Test]
    public void FromInt64_WithValidValue_SetsValue()
    {
        var version = StreamVersion.FromInt64(123);
        version.ToInt64().ShouldBe(123);
    }

    [Test]
    public void FromInt64_WithInvalidValue_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StreamVersion.FromInt64(-2));
    }

    [Test]
    public void ToInt64_WhenInstanceIsZero_ReturnsZero()
    {
        StreamVersion.Zero.ToInt64().ShouldBe(0);
    }

    [Test]
    public void ToInt64_WhenInstanceIsNone_ReturnsNegativeOne()
    {
        StreamVersion.None.ToInt64().ShouldBe(-1);
    }

    [Test]
    public void Next_WhenSpecific_ReturnsIncremented()
    {
        var v = StreamVersion.FromInt64(10);
        v.Next().ToInt64().ShouldBe(11);
    }

    [Test]
    public void Next_WhenNone_ReturnsZero()
    {
        var v = StreamVersion.None;
        v.Next().ToInt64().ShouldBe(0);
    }

    [Test]
    public void Add_WithPositiveValue_IncreasesByValue()
    {
        var v = StreamVersion.FromInt64(5);
        v.Add(3).ToInt64().ShouldBe(8);
    }

    [Test]
    public void Add_WithZero_ReturnsSameValue()
    {
        var v = StreamVersion.FromInt64(42);
        v.Add(0).ToInt64().ShouldBe(42);
    }

    [Test]
    public void Add_WithNegativeValue_DecreasesByValue()
    {
        var v = StreamVersion.FromInt64(5);
        v.Add(-3).ToInt64().ShouldBe(2);
    }

    [Test]
    public void Add_WhenResultIsMinusOne_ReturnsNone()
    {
        var v = StreamVersion.FromInt64(0);
        v.Add(-1).ShouldBe(StreamVersion.None);
    }

    [Test]
    public void Add_WhenResultWouldBeLessThanMinusOne_ThrowsArgumentOutOfRange()
    {
        var v = StreamVersion.FromInt64(0);
        Should.Throw<ArgumentOutOfRangeException>(() => v.Add(-2));
    }

    [Test]
    public void Add_WhenOverflowOccurs_ThrowsOverflow()
    {
        var v = StreamVersion.FromInt64(long.MaxValue);
        Should.Throw<OverflowException>(() => v.Add(1));
    }

    [Test]
    public void Add_WhenUnderflowOccurs_ThrowsOverflow()
    {
        var v = StreamVersion.FromInt64(-1);
        Should.Throw<OverflowException>(() => v.Add(long.MinValue));
    }

    [Test]
    public void ToString_WithNoneOrValue_ReturnsExpectedResults()
    {
        StreamVersion.None.ToString().ShouldBe("None");
        StreamVersion.FromInt64(99).ToString().ShouldBe("99");
    }

    [Test]
    public void Equals_WithSameOrDifferentValues_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(7);
        var b = StreamVersion.FromInt64(7);
        var c = StreamVersion.FromInt64(8);

        a.Equals(b).ShouldBeTrue();
        a.Equals(c).ShouldBeFalse();
    }

    [Test]
    public void GetHashCode_WithSameValue_ReturnsSameCode()
    {
        var a = StreamVersion.FromInt64(123);
        var b = StreamVersion.FromInt64(123);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Test]
    public void CompareTo_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(1);
        var b = StreamVersion.FromInt64(2);

        a.CompareTo(b).ShouldBeLessThan(0);
        b.CompareTo(a).ShouldBeGreaterThan(0);
        a.CompareTo(a).ShouldBe(0);
    }

    [Test]
    public void LessThan_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (a < b).ShouldBeTrue();
        (b < a).ShouldBeFalse();
        (a < c).ShouldBeFalse();
    }

    [Test]
    public void GreaterThan_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (b > a).ShouldBeTrue();
        (a > b).ShouldBeFalse();
        (a > c).ShouldBeFalse();
    }

    [Test]
    public void LessThanOrEqual_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (a <= b).ShouldBeTrue();
        (b <= a).ShouldBeFalse();
        (a <= c).ShouldBeTrue();
    }

    [Test]
    public void GreaterThanOrEqual_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (b >= a).ShouldBeTrue();
        (a >= b).ShouldBeFalse();
        (a >= c).ShouldBeTrue();
    }

    [Test]
    public void EqualityOperator_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (a == c).ShouldBeTrue();
        (a == b).ShouldBeFalse();
    }

    [Test]
    public void InequalityOperator_WithOperandPermutations_ReturnsExpectedResults()
    {
        var a = StreamVersion.FromInt64(3);
        var b = StreamVersion.FromInt64(4);
        var c = StreamVersion.FromInt64(3);

        (a != b).ShouldBeTrue();
        (a != c).ShouldBeFalse();
    }
}