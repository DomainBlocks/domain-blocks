using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class ObservedStreamStateTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsDoesNotExist()
    {
        default(ObservedStreamState<StreamPosition>).ShouldBe(ObservedStreamState.DoesNotExist<StreamPosition>());
    }

    [Test]
    public void HasVersion_WhenStreamDoesNotExist_IsFalse()
    {
        ObservedStreamState.DoesNotExist<StreamPosition>().HasVersion.ShouldBeFalse();
    }

    [Test]
    public void HasVersion_WhenAtVersion_IsTrue()
    {
        ObservedStreamState.AtVersion(new StreamPosition(42)).HasVersion.ShouldBeTrue();
    }

    [Test]
    public void Version_WhenStreamDoesNotExist_Throws()
    {
        Should.Throw<InvalidOperationException>(() => ObservedStreamState.DoesNotExist<StreamPosition>().Version);
    }

    [Test]
    public void Version_WhenAtVersion_ReturnsVersion()
    {
        var version = new StreamPosition(42);

        ObservedStreamState.AtVersion(version).Version.ShouldBe(version);
    }

    [Test]
    public void AtVersion_WhenVersionIsNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ObservedStreamState.AtVersion<string>(null!));
    }

    [Test]
    public void ToString_WhenStreamDoesNotExist_ReturnsName()
    {
        ObservedStreamState.DoesNotExist<StreamPosition>().ToString().ShouldBe("DoesNotExist");
    }

    [Test]
    public void ToString_WhenAtVersion_ReturnsNumericValue()
    {
        ObservedStreamState.AtVersion(new StreamPosition(99)).ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        ObservedStreamState.AtVersion(new StreamPosition(1))
            .Equals(ObservedStreamState.AtVersion(new StreamPosition(1)))
            .ShouldBeTrue();

        ObservedStreamState.DoesNotExist<StreamPosition>()
            .Equals(default)
            .ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        ObservedStreamState.AtVersion(new StreamPosition(1))
            .Equals(ObservedStreamState.AtVersion(new StreamPosition(2)))
            .ShouldBeFalse();

        ObservedStreamState.DoesNotExist<StreamPosition>()
            .Equals(ObservedStreamState.AtVersion(new StreamPosition(1)))
            .ShouldBeFalse();
    }
}