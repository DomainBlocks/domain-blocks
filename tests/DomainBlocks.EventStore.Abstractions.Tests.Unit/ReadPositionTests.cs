using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class ReadPositionTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsStart()
    {
        default(ReadPosition<StreamVersion>).ShouldBe(ReadPosition<StreamVersion>.Start);
        new ReadPosition<StreamVersion>().ShouldBe(ReadPosition<StreamVersion>.Start);
    }

    [Test]
    public void Version_WhenStartOrEnd_IsNull()
    {
        ReadPosition<StreamVersion>.Start.Specific.ShouldBeNull();
        ReadPosition<StreamVersion>.End.Specific.ShouldBeNull();
    }

    [Test]
    public void Version_WhenSpecific_IsSpecific()
    {
        var version = new StreamVersion(123);
        var position = ReadPosition<StreamVersion>.At(version);
        position.Specific.ShouldBe(version);
    }

    [Test]
    public void ToString_WhenNonSpecific_ReturnsName()
    {
        ReadPosition<StreamVersion>.Start.ToString().ShouldBe("Start");
        ReadPosition<StreamVersion>.End.ToString().ShouldBe("End");
    }

    [Test]
    public void ToString_ForSpecific_ReturnsNumericValue()
    {
        var specific = ReadPosition<StreamVersion>.At(new StreamVersion(99));
        specific.ToString().ShouldBe("Specific=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = ReadPosition<StreamVersion>.At(new StreamVersion(1));
        var b = ReadPosition<StreamVersion>.At(new StreamVersion(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = ReadPosition<StreamVersion>.At(new StreamVersion(1));
        var b = ReadPosition<StreamVersion>.At(new StreamVersion(2));

        a.Equals(b).ShouldBeFalse();
        ReadPosition<StreamVersion>.Start.Equals(ReadPosition<StreamVersion>.End).ShouldBeFalse();
        ReadPosition<StreamVersion>.Start.Equals(a).ShouldBeFalse();
        ReadPosition<StreamVersion>.End.Equals(a).ShouldBeFalse();
    }
}