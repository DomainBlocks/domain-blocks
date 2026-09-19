using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class SubscriptionOriginTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_NamesNoOrigin()
    {
        var origin = default(SubscriptionOrigin<LogPosition>);

        (origin is null).ShouldBeTrue();
        origin.HasValue.ShouldBeFalse();
        origin.Value.ShouldBeNull();
    }

    [Test]
    public void Conversion_FromStart_IsStartCase()
    {
        SubscriptionOrigin<LogPosition> origin = SubscriptionOrigin.Start;

        (origin is SequenceStart).ShouldBeTrue();
        (origin is SequenceEnd).ShouldBeFalse();
        origin.Value.ShouldBe(new SequenceStart());
    }

    [Test]
    public void Conversion_FromEnd_IsEndCase()
    {
        SubscriptionOrigin<LogPosition> origin = SubscriptionOrigin.End;

        (origin is SequenceEnd).ShouldBeTrue();
        (origin is SequenceStart).ShouldBeFalse();
        origin.Value.ShouldBe(new SequenceEnd());
    }

    [Test]
    public void Conversion_FromAfter_IsAfterCase()
    {
        SubscriptionOrigin<LogPosition> origin = SubscriptionOrigin.After(new LogPosition(42));

        (origin is SubscriptionOrigin<LogPosition>.After after && after.Position == new LogPosition(42)).ShouldBeTrue();
        origin.Value.ShouldBe(new SubscriptionOrigin<LogPosition>.After(new LogPosition(42)));
    }

    [Test]
    public void Constructor_WhenPositionIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => new SubscriptionOrigin<string>(new SubscriptionOrigin<string>.After(null!)));
    }

    [Test]
    public void TryGetValue_WhenAfter_ReturnsOnlyAfter()
    {
        SubscriptionOrigin<LogPosition> origin = SubscriptionOrigin.After(new LogPosition(42));

        origin.TryGetValue(out SubscriptionOrigin<LogPosition>.After after).ShouldBeTrue();
        after.Position.ShouldBe(new LogPosition(42));
        origin.TryGetValue(out SequenceStart _).ShouldBeFalse();
        origin.TryGetValue(out SequenceEnd _).ShouldBeFalse();
    }

    [Test]
    public void Resolve_WhenNoOriginIsNamed_ReturnsEnd()
    {
        default(SubscriptionOrigin<LogPosition>)
            .Resolve()
            .ShouldBe(new SubscriptionOrigin<LogPosition>(SubscriptionOrigin.End));
    }

    [Test]
    public void Resolve_WhenOriginIsNamed_ReturnsIt()
    {
        SubscriptionOrigin<LogPosition> start = SubscriptionOrigin.Start;
        SubscriptionOrigin<LogPosition> after = SubscriptionOrigin.After(new LogPosition(42));

        start.Resolve().ShouldBe(start);
        after.Resolve().ShouldBe(after);
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that.
    [Test]
    public void Switch_WhenAfter_DoesNotAllocate()
    {
        SubscriptionOrigin<LogPosition> origin = SubscriptionOrigin.After(new LogPosition(42));
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
        SubscriptionOrigin<LogPosition> start = SubscriptionOrigin.Start;
        SubscriptionOrigin<LogPosition> end = SubscriptionOrigin.End;
        SubscriptionOrigin<LogPosition> after = SubscriptionOrigin.After(new LogPosition(42));

        start.ToString().ShouldBe("Start");
        end.ToString().ShouldBe("End");
        after.ToString().ShouldBe("After(42)");
        default(SubscriptionOrigin<LogPosition>).ToString().ShouldBe("Unspecified");
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static ulong PositionOf(SubscriptionOrigin<LogPosition> origin)
    {
        return origin switch
        {
            SubscriptionOrigin<LogPosition>.After after => after.Position.Value,
            SequenceStart => 0,
            SequenceEnd => 0,
            null => 0
        };
    }
}