using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class ReadOriginTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_NamesNoOrigin()
    {
        var origin = default(ReadOrigin<StreamPosition>);

        (origin is null).ShouldBeTrue();
        origin.HasValue.ShouldBeFalse();
        origin.Value.ShouldBeNull();
    }

    [Test]
    public void Conversion_FromStart_IsStartCase()
    {
        ReadOrigin<StreamPosition> origin = ReadOrigin.Start;

        (origin is SequenceStart).ShouldBeTrue();
        (origin is SequenceEnd).ShouldBeFalse();
        origin.Value.ShouldBe(new SequenceStart());
    }

    [Test]
    public void Conversion_FromEnd_IsEndCase()
    {
        ReadOrigin<StreamPosition> origin = ReadOrigin.End;

        (origin is SequenceEnd).ShouldBeTrue();
        (origin is SequenceStart).ShouldBeFalse();
        origin.Value.ShouldBe(new SequenceEnd());
    }

    [Test]
    public void Conversion_FromAt_IsAtCase()
    {
        ReadOrigin<StreamPosition> origin = ReadOrigin.At(new StreamPosition(42));

        (origin is ReadOrigin<StreamPosition>.At at && at.Position == new StreamPosition(42)).ShouldBeTrue();
        origin.Value.ShouldBe(new ReadOrigin<StreamPosition>.At(new StreamPosition(42)));
    }

    [Test]
    public void Constructor_WhenPositionIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ReadOrigin<string>(new ReadOrigin<string>.At(null!)));
    }

    [Test]
    public void TryGetValue_WhenAt_ReturnsOnlyAt()
    {
        ReadOrigin<StreamPosition> origin = ReadOrigin.At(new StreamPosition(42));

        origin.TryGetValue(out ReadOrigin<StreamPosition>.At at).ShouldBeTrue();
        at.Position.ShouldBe(new StreamPosition(42));
        origin.TryGetValue(out SequenceStart _).ShouldBeFalse();
        origin.TryGetValue(out SequenceEnd _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_WhenNoOriginIsNamed_ReturnsNothing()
    {
        var origin = default(ReadOrigin<StreamPosition>);

        origin.TryGetValue(out ReadOrigin<StreamPosition>.At _).ShouldBeFalse();
        origin.TryGetValue(out SequenceStart _).ShouldBeFalse();
        origin.TryGetValue(out SequenceEnd _).ShouldBeFalse();
    }

    [Test]
    public void ResolveFor_WhenNoOriginIsNamed_ReturnsWhereTheDirectionBegins()
    {
        var origin = default(ReadOrigin<StreamPosition>);

        origin.ResolveFor(ReadDirection.Forward).ShouldBe(new ReadOrigin<StreamPosition>(ReadOrigin.Start));
        origin.ResolveFor(ReadDirection.Backward).ShouldBe(new ReadOrigin<StreamPosition>(ReadOrigin.End));
    }

    [Test]
    public void ResolveFor_WhenOriginIsNamed_ReturnsIt()
    {
        ReadOrigin<StreamPosition> start = ReadOrigin.Start;
        ReadOrigin<StreamPosition> at = ReadOrigin.At(new StreamPosition(42));

        start.ResolveFor(ReadDirection.Backward).ShouldBe(start);
        at.ResolveFor(ReadDirection.Backward).ShouldBe(at);
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that.
    [Test]
    public void Switch_WhenAt_DoesNotAllocate()
    {
        ReadOrigin<StreamPosition> origin = ReadOrigin.At(new StreamPosition(42));
        PositionOf(origin);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ulong total = 0;

        for (var i = 0; i < 1_000; i++)
            total += PositionOf(origin);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.ShouldBe(42_000ul);
        allocated.ShouldBe(0);
    }

    [Test]
    public void ToString_ForEachCase_ReturnsName()
    {
        ReadOrigin<StreamPosition> start = ReadOrigin.Start;
        ReadOrigin<StreamPosition> end = ReadOrigin.End;
        ReadOrigin<StreamPosition> at = ReadOrigin.At(new StreamPosition(42));

        start.ToString().ShouldBe("Start");
        end.ToString().ShouldBe("End");
        at.ToString().ShouldBe("At(42)");
        default(ReadOrigin<StreamPosition>).ToString().ShouldBe("Unspecified");
    }

    [Test]
    public void Equals_WhenCasesDiffer_ReturnsFalse()
    {
        ReadOrigin<StreamPosition> start = ReadOrigin.Start;
        ReadOrigin<StreamPosition> end = ReadOrigin.End;
        ReadOrigin<StreamPosition> at = ReadOrigin.At(new StreamPosition(1));

        start.Equals(end).ShouldBeFalse();
        start.Equals(default).ShouldBeFalse();
        at.Equals(new ReadOrigin<StreamPosition>(ReadOrigin.At(new StreamPosition(2)))).ShouldBeFalse();
        at.Equals(new ReadOrigin<StreamPosition>(ReadOrigin.At(new StreamPosition(1)))).ShouldBeTrue();
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static ulong PositionOf(ReadOrigin<StreamPosition> origin)
    {
        return origin switch
        {
            ReadOrigin<StreamPosition>.At at => at.Position.Value,
            SequenceStart => 0,
            SequenceEnd => 0,
            null => 0
        };
    }
}