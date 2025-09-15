using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Abstractions.Tests.Unit;

public class StreamPositionTests
{
    [Test]
    public void Instance_WhenDefaultConstructed_EqualsStart()
    {
        default(StreamPosition).ShouldBe(StreamPosition.Start);
    }

    [Test]
    public void Kind_WhenInstanceIsStart_IsStart()
    {
        StreamPosition.Start.Kind.ShouldBe(StreamPositionKind.Start);
        StreamPosition.Start.IsStart.ShouldBeTrue();
        StreamPosition.Start.IsEnd.ShouldBeFalse();
        StreamPosition.Start.IsSpecificVersion.ShouldBeFalse();
    }

    [Test]
    public void Kind_WhenInstanceIsEnd_IsEnd()
    {
        StreamPosition.End.Kind.ShouldBe(StreamPositionKind.End);
        StreamPosition.End.IsStart.ShouldBeFalse();
        StreamPosition.End.IsEnd.ShouldBeTrue();
        StreamPosition.End.IsSpecificVersion.ShouldBeFalse();
    }

    [Test]
    public void Kind_WhenInstanceIsSpecificVersion_IsSpecificVersion()
    {
        var version = StreamVersion.FromInt64(42);
        var position = StreamPosition.At(version);

        position.Kind.ShouldBe(StreamPositionKind.SpecificVersion);
        position.IsStart.ShouldBeFalse();
        position.IsEnd.ShouldBeFalse();
        position.IsSpecificVersion.ShouldBeTrue();
    }

    [Test]
    public void At_WhenVersionIsNone_ReturnsStart()
    {
        StreamPosition.At(StreamVersion.None).ShouldBe(StreamPosition.Start);
    }

    [Test]
    public void Version_WhenStartOrEnd_IsNull()
    {
        StreamPosition.Start.Version.ShouldBeNull();
        StreamPosition.End.Version.ShouldBeNull();
    }

    [Test]
    public void Version_WhenSpecificVersion_IsSpecificVersion()
    {
        var version = StreamVersion.FromInt64(123);
        var position = StreamPosition.At(version);
        position.Version.ShouldBe(version);
    }

    [Test]
    public void ToString_WhenNonSpecificVersion_ReturnsName()
    {
        StreamPosition.Start.ToString().ShouldBe("Start");
        StreamPosition.End.ToString().ShouldBe("End");
    }

    [Test]
    public void ToString_ForSpecificVersion_ReturnsNumericValue()
    {
        var specific = StreamPosition.At(StreamVersion.FromInt64(99));
        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = StreamPosition.At(StreamVersion.FromInt64(1));
        var b = StreamPosition.At(StreamVersion.FromInt64(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = StreamPosition.At(StreamVersion.FromInt64(1));
        var b = StreamPosition.At(StreamVersion.FromInt64(2));

        a.Equals(b).ShouldBeFalse();
        StreamPosition.Start.Equals(StreamPosition.End).ShouldBeFalse();
        StreamPosition.Start.Equals(a).ShouldBeFalse();
        StreamPosition.End.Equals(a).ShouldBeFalse();
    }
}