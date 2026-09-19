using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class ObservedStreamStateTests
{
    // Typed as the union, so that members called on them are the union's and not the case's.
    private static readonly ObservedStreamState<StreamPosition> DoesNotExist = ObservedStreamState.DoesNotExist;

    private static ObservedStreamState<StreamPosition> AtVersion(ulong version)
    {
        return new StreamPosition(version);
    }

    [Test]
    public void Instance_WhenDefaultConstructed_IsNotObserved()
    {
        var state = default(ObservedStreamState<StreamPosition>);

        (state is null).ShouldBeTrue();
        state.HasValue.ShouldBeFalse();
        state.Value.ShouldBeNull();
        state.ShouldNotBe(DoesNotExist);
    }

    [Test]
    public void DoesNotExist_WhenMatched_IsDoesNotExistCase()
    {
        var state = DoesNotExist;

        (state is StreamDoesNotExist).ShouldBeTrue();
        (state is StreamPosition).ShouldBeFalse();
        state.HasValue.ShouldBeTrue();
        state.Value.ShouldBe(new StreamDoesNotExist());
    }

    [Test]
    public void AtVersion_WhenMatched_IsVersionCase()
    {
        var state = AtVersion(42);

        (state is StreamPosition version && version == new StreamPosition(42)).ShouldBeTrue();
        (state is StreamDoesNotExist).ShouldBeFalse();
        state.HasValue.ShouldBeTrue();
        state.Value.ShouldBe(new StreamPosition(42));
    }

    [Test]
    public void Constructor_WhenVersionIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ObservedStreamState<string>(null!));
    }

    [Test]
    public void Conversion_FromVersion_CreatesAtVersion()
    {
        ObservedStreamState<StreamPosition> state = new StreamPosition(42);

        state.ShouldBe(AtVersion(42));
    }

    [Test]
    public void Conversion_FromStreamDoesNotExist_CreatesDoesNotExist()
    {
        ObservedStreamState<StreamPosition> state = new StreamDoesNotExist();

        state.ShouldBe(DoesNotExist);
    }

    [Test]
    public void TryGetValue_WhenAtVersion_ReturnsOnlyVersion()
    {
        var state = AtVersion(42);

        state.TryGetValue(out StreamPosition version).ShouldBeTrue();
        version.ShouldBe(new StreamPosition(42));
        state.TryGetValue(out StreamDoesNotExist _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_WhenStreamDoesNotExist_ReturnsOnlyDoesNotExist()
    {
        var state = DoesNotExist;

        state.TryGetValue(out StreamDoesNotExist _).ShouldBeTrue();
        state.TryGetValue(out StreamPosition _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_WhenNotObserved_ReturnsNeither()
    {
        var state = default(ObservedStreamState<StreamPosition>);

        state.TryGetValue(out StreamDoesNotExist _).ShouldBeFalse();
        state.TryGetValue(out StreamPosition _).ShouldBeFalse();
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that.
    [Test]
    public void Switch_WhenAtVersion_DoesNotAllocate()
    {
        var state = AtVersion(42);
        VersionOf(state);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ulong total = 0;

        for (var i = 0; i < 1_000; i++)
            total += VersionOf(state);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.ShouldBe(42_000ul);
        allocated.ShouldBe(0);
    }

    [Test]
    public void ToString_WhenStreamDoesNotExist_ReturnsName()
    {
        DoesNotExist.ToString().ShouldBe("DoesNotExist");
    }

    [Test]
    public void ToString_WhenAtVersion_ReturnsNumericValue()
    {
        AtVersion(99).ToString().ShouldBe("Version=99");
    }

    [Test]
    public void ToString_WhenNotObserved_ReturnsName()
    {
        default(ObservedStreamState<StreamPosition>).ToString().ShouldBe("NotObserved");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        AtVersion(1)
            .Equals(AtVersion(1))
            .ShouldBeTrue();

        DoesNotExist
            .Equals(new ObservedStreamState<StreamPosition>(new StreamDoesNotExist()))
            .ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        AtVersion(1)
            .Equals(AtVersion(2))
            .ShouldBeFalse();

        DoesNotExist
            .Equals(AtVersion(1))
            .ShouldBeFalse();

        DoesNotExist
            .Equals(default)
            .ShouldBeFalse();
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static ulong VersionOf(ObservedStreamState<StreamPosition> state)
    {
        return state switch
        {
            StreamPosition version => version.Value,
            StreamDoesNotExist => 0,
            null => 0
        };
    }
}