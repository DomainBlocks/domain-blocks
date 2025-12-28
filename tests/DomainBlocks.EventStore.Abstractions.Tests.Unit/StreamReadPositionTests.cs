using DomainBlocks.EventStore.Abstractions.Events;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamReadPositionTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsStart()
    {
        default(StreamReadPosition).ShouldBe(StreamReadPosition.Start);
        new StreamReadPosition().ShouldBe(StreamReadPosition.Start);
    }

    [Test]
    public void Version_WhenStartOrEnd_IsNull()
    {
        StreamReadPosition.Start.Version.ShouldBeNull();
        StreamReadPosition.End.Version.ShouldBeNull();
    }

    [Test]
    public void Version_WhenSpecificVersion_IsSpecificVersion()
    {
        var version = new StreamVersion(123);
        var position = StreamReadPosition.At(version);
        position.Version.ShouldBe(version);
    }

    [Test]
    public void ToString_WhenNonSpecificVersion_ReturnsName()
    {
        StreamReadPosition.Start.ToString().ShouldBe("Start");
        StreamReadPosition.End.ToString().ShouldBe("End");
    }

    [Test]
    public void ToString_ForSpecificVersion_ReturnsNumericValue()
    {
        var specific = StreamReadPosition.At(new StreamVersion(99));
        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = StreamReadPosition.At(new StreamVersion(1));
        var b = StreamReadPosition.At(new StreamVersion(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = StreamReadPosition.At(new StreamVersion(1));
        var b = StreamReadPosition.At(new StreamVersion(2));

        a.Equals(b).ShouldBeFalse();
        StreamReadPosition.Start.Equals(StreamReadPosition.End).ShouldBeFalse();
        StreamReadPosition.Start.Equals(a).ShouldBeFalse();
        StreamReadPosition.End.Equals(a).ShouldBeFalse();
    }
}