using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class ExpectedStreamStateTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsAny()
    {
        default(ExpectedStreamState<StreamPosition>).ShouldBe(ExpectedStreamState.Any<StreamPosition>());
    }

    [Test]
    public void HasVersion_WhenNonSpecificState_IsFalse()
    {
        ExpectedStreamState.Any<StreamPosition>().HasVersion.ShouldBeFalse();
        ExpectedStreamState.Exists<StreamPosition>().HasVersion.ShouldBeFalse();
        ExpectedStreamState.DoesNotExist<StreamPosition>().HasVersion.ShouldBeFalse();
    }

    [Test]
    public void Version_WhenNonSpecificState_Throws()
    {
        Should.Throw<InvalidOperationException>(() => ExpectedStreamState.Any<StreamPosition>().Version);
        Should.Throw<InvalidOperationException>(() => ExpectedStreamState.Exists<StreamPosition>().Version);
        Should.Throw<InvalidOperationException>(() => ExpectedStreamState.DoesNotExist<StreamPosition>().Version);
    }

    [Test]
    public void Version_WhenSpecificVersion_IsSpecificVersion()
    {
        var version = new StreamPosition(42);
        var expected = ExpectedStreamState.AtVersion(version);

        expected.HasVersion.ShouldBeTrue();
        expected.Version.ShouldBe(version);
    }

    [Test]
    public void Matches_WhenAnyExpectedState_ReturnsTrueForAnyObservedState()
    {
        var expected = ExpectedStreamState.Any<StreamPosition>();

        expected.Matches(ObservedStreamState.DoesNotExist<StreamPosition>()).ShouldBeTrue();
        expected.Matches(ObservedStreamState.AtVersion(new StreamPosition(42))).ShouldBeTrue();
    }

    [Test]
    public void Matches_WhenStreamDoesNotExistExpectedState_ReturnsTrueOnlyForNonexistentStream()
    {
        var expected = ExpectedStreamState.DoesNotExist<StreamPosition>();

        expected.Matches(ObservedStreamState.DoesNotExist<StreamPosition>()).ShouldBeTrue();
        expected.Matches(ObservedStreamState.AtVersion(new StreamPosition(42))).ShouldBeFalse();
    }

    [Test]
    public void Matches_WhenStreamExistsExpectedState_ReturnsTrueOnlyForExistingStream()
    {
        var expected = ExpectedStreamState.Exists<StreamPosition>();

        expected.Matches(ObservedStreamState.DoesNotExist<StreamPosition>()).ShouldBeFalse();
        expected.Matches(ObservedStreamState.AtVersion(new StreamPosition(42))).ShouldBeTrue();
    }

    [Test]
    public void Matches_WhenSpecificVersionExpectedState_ReturnsTrueOnlyForMatchingVersion()
    {
        var expected = ExpectedStreamState.AtVersion(new StreamPosition(42));

        expected.Matches(ObservedStreamState.DoesNotExist<StreamPosition>()).ShouldBeFalse();
        expected.Matches(ObservedStreamState.AtVersion(new StreamPosition(41))).ShouldBeFalse();
        expected.Matches(ObservedStreamState.AtVersion(new StreamPosition(42))).ShouldBeTrue();
    }

    [Test]
    public void ToString_WhenNonSpecificVersion_ReturnsName()
    {
        ExpectedStreamState.Any<StreamPosition>().ToString().ShouldBe("Any");
        ExpectedStreamState.Exists<StreamPosition>().ToString().ShouldBe("Exists");
        ExpectedStreamState.DoesNotExist<StreamPosition>().ToString().ShouldBe("DoesNotExist");
    }

    [Test]
    public void ToString_ForSpecificVersion_ReturnsNumericValue()
    {
        var specific = ExpectedStreamState.AtVersion(new StreamPosition(99));

        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = ExpectedStreamState.AtVersion(new StreamPosition(1));
        var b = ExpectedStreamState.AtVersion(new StreamPosition(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = ExpectedStreamState.AtVersion(new StreamPosition(1));
        var b = ExpectedStreamState.AtVersion(new StreamPosition(2));

        a.Equals(b).ShouldBeFalse();
        a.Equals(ExpectedStreamState.Any<StreamPosition>()).ShouldBeFalse();
        a.Equals(ExpectedStreamState.Exists<StreamPosition>()).ShouldBeFalse();
        a.Equals(ExpectedStreamState.DoesNotExist<StreamPosition>()).ShouldBeFalse();

        ExpectedStreamState.Any<StreamPosition>()
            .Equals(ExpectedStreamState.Exists<StreamPosition>())
            .ShouldBeFalse();

        ExpectedStreamState.Any<StreamPosition>()
            .Equals(ExpectedStreamState.DoesNotExist<StreamPosition>())
            .ShouldBeFalse();

        ExpectedStreamState.Exists<StreamPosition>()
            .Equals(ExpectedStreamState.DoesNotExist<StreamPosition>())
            .ShouldBeFalse();
    }
}