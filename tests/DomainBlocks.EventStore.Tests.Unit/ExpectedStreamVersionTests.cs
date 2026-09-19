using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class ExpectedStreamStateTests
{
    // Typed as the union, so that members called on them are the union's and not the case's.
    private static readonly ExpectedStreamState<StreamPosition> Any = default;
    private static readonly ExpectedStreamState<StreamPosition> DoesNotExist = ExpectedStreamState.DoesNotExist;
    private static readonly ExpectedStreamState<StreamPosition> Exists = ExpectedStreamState.Exists;
    private static readonly ObservedStreamState<StreamPosition> ObservedDoesNotExist = ObservedStreamState.DoesNotExist;

    private static ExpectedStreamState<StreamPosition> AtVersion(ulong version)
    {
        return new StreamPosition(version);
    }

    private static ObservedStreamState<StreamPosition> ObservedAtVersion(ulong version)
    {
        return new StreamPosition(version);
    }

    [Test]
    public void Instance_WhenDefaultConstructed_EqualsAny()
    {
        default(ExpectedStreamState<StreamPosition>).ShouldBe(Any);
    }

    [Test]
    public void Any_WhenMatched_IsNull()
    {
        var expected = Any;

        (expected is null).ShouldBeTrue();
        expected.HasValue.ShouldBeFalse();
        expected.Value.ShouldBeNull();
    }

    [Test]
    public void DoesNotExist_WhenMatched_IsDoesNotExistCase()
    {
        var expected = DoesNotExist;

        (expected is StreamDoesNotExist).ShouldBeTrue();
        expected.HasValue.ShouldBeTrue();
        expected.Value.ShouldBe(new StreamDoesNotExist());
    }

    [Test]
    public void Exists_WhenMatched_IsExistsCase()
    {
        var expected = Exists;

        (expected is StreamExists).ShouldBeTrue();
        expected.HasValue.ShouldBeTrue();
        expected.Value.ShouldBe(new StreamExists());
    }

    [Test]
    public void AtVersion_WhenMatched_IsVersionCase()
    {
        var expected = AtVersion(42);

        (expected is StreamPosition version && version == new StreamPosition(42)).ShouldBeTrue();
        expected.HasValue.ShouldBeTrue();
        expected.Value.ShouldBe(new StreamPosition(42));
    }

    [Test]
    public void Constructor_WhenVersionIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ExpectedStreamState<string>(null!));
    }

    [Test]
    public void Conversion_FromCase_CreatesMatchingState()
    {
        ExpectedStreamState<StreamPosition> doesNotExist = new StreamDoesNotExist();
        ExpectedStreamState<StreamPosition> exists = new StreamExists();
        ExpectedStreamState<StreamPosition> atVersion = new StreamPosition(42);

        doesNotExist.ShouldBe(DoesNotExist);
        exists.ShouldBe(Exists);
        atVersion.ShouldBe(AtVersion(42));
    }

    [Test]
    public void TryGetValue_WhenAtVersion_ReturnsOnlyVersion()
    {
        var expected = AtVersion(42);

        expected.TryGetValue(out StreamPosition version).ShouldBeTrue();
        version.ShouldBe(new StreamPosition(42));
        expected.TryGetValue(out StreamDoesNotExist _).ShouldBeFalse();
        expected.TryGetValue(out StreamExists _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_WhenAny_ReturnsNothing()
    {
        var expected = Any;

        expected.TryGetValue(out StreamPosition _).ShouldBeFalse();
        expected.TryGetValue(out StreamDoesNotExist _).ShouldBeFalse();
        expected.TryGetValue(out StreamExists _).ShouldBeFalse();
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that.
    [Test]
    public void Switch_WhenAtVersion_DoesNotAllocate()
    {
        var expected = AtVersion(42);
        VersionOf(expected);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ulong total = 0;

        for (var i = 0; i < 1_000; i++)
            total += VersionOf(expected);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.ShouldBe(42_000ul);
        allocated.ShouldBe(0);
    }

    [Test]
    public void Matches_WhenAnyExpectedState_ReturnsTrueForAnyObservedState()
    {
        var expected = Any;

        expected.Matches(ObservedDoesNotExist).ShouldBeTrue();
        expected.Matches(ObservedAtVersion(42)).ShouldBeTrue();
        expected.Matches(default).ShouldBeTrue();
    }

    [Test]
    public void Matches_WhenStreamWasNotObserved_ReturnsTrueOnlyForAny()
    {
        DoesNotExist.Matches(default).ShouldBeFalse();
        Exists.Matches(default).ShouldBeFalse();
        AtVersion(42).Matches(default).ShouldBeFalse();
    }

    [Test]
    public void Matches_WhenStreamDoesNotExistExpectedState_ReturnsTrueOnlyForNonexistentStream()
    {
        var expected = DoesNotExist;

        expected.Matches(ObservedDoesNotExist).ShouldBeTrue();
        expected.Matches(ObservedAtVersion(42)).ShouldBeFalse();
    }

    [Test]
    public void Matches_WhenStreamExistsExpectedState_ReturnsTrueOnlyForExistingStream()
    {
        var expected = Exists;

        expected.Matches(ObservedDoesNotExist).ShouldBeFalse();
        expected.Matches(ObservedAtVersion(42)).ShouldBeTrue();
    }

    [Test]
    public void Matches_WhenSpecificVersionExpectedState_ReturnsTrueOnlyForMatchingVersion()
    {
        var expected = AtVersion(42);

        expected.Matches(ObservedDoesNotExist).ShouldBeFalse();
        expected.Matches(ObservedAtVersion(41)).ShouldBeFalse();
        expected.Matches(ObservedAtVersion(42)).ShouldBeTrue();
    }

    [Test]
    public void ToString_WhenNonSpecificVersion_ReturnsName()
    {
        Any.ToString().ShouldBe("Any");
        Exists.ToString().ShouldBe("Exists");
        DoesNotExist.ToString().ShouldBe("DoesNotExist");
    }

    [Test]
    public void ToString_ForSpecificVersion_ReturnsNumericValue()
    {
        var specific = AtVersion(99);

        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = AtVersion(1);
        var b = AtVersion(1);

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = AtVersion(1);
        var b = AtVersion(2);

        a.Equals(b).ShouldBeFalse();
        a.Equals(Any).ShouldBeFalse();
        a.Equals(Exists).ShouldBeFalse();
        a.Equals(DoesNotExist).ShouldBeFalse();

        Any
            .Equals(Exists)
            .ShouldBeFalse();

        Any
            .Equals(DoesNotExist)
            .ShouldBeFalse();

        Exists
            .Equals(DoesNotExist)
            .ShouldBeFalse();
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static ulong VersionOf(ExpectedStreamState<StreamPosition> expected)
    {
        return expected switch
        {
            StreamPosition version => version.Value,
            StreamDoesNotExist => 0,
            StreamExists => 0,
            null => 0
        };
    }
}