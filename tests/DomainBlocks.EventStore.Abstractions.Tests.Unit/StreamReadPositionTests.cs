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
    }

    [Test]
    public void Kind_WhenInstanceIsStart_IsStart()
    {
        StreamReadPosition.Start.Kind.ShouldBe(StreamReadPositionKind.Start);
        StreamReadPosition.Start.IsStart.ShouldBeTrue();
        StreamReadPosition.Start.IsEnd.ShouldBeFalse();
        StreamReadPosition.Start.IsSpecificVersion.ShouldBeFalse();
    }

    [Test]
    public void Kind_WhenInstanceIsEnd_IsEnd()
    {
        StreamReadPosition.End.Kind.ShouldBe(StreamReadPositionKind.End);
        StreamReadPosition.End.IsStart.ShouldBeFalse();
        StreamReadPosition.End.IsEnd.ShouldBeTrue();
        StreamReadPosition.End.IsSpecificVersion.ShouldBeFalse();
    }

    [Test]
    public void Kind_WhenInstanceIsSpecificVersion_IsSpecificVersion()
    {
        var version = StreamVersion.FromInt64(42);
        var position = StreamReadPosition.At(version);

        position.Kind.ShouldBe(StreamReadPositionKind.SpecificVersion);
        position.IsStart.ShouldBeFalse();
        position.IsEnd.ShouldBeFalse();
        position.IsSpecificVersion.ShouldBeTrue();
    }

    [Test]
    public void At_WhenVersionIsNone_ReturnsStart()
    {
        StreamReadPosition.At(StreamVersion.None).ShouldBe(StreamReadPosition.Start);
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
        var version = StreamVersion.FromInt64(123);
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
        var specific = StreamReadPosition.At(StreamVersion.FromInt64(99));
        specific.ToString().ShouldBe("Version=99");
    }

    [Test]
    public void Equals_WhenValuesAreSame_ReturnsTrue()
    {
        var a = StreamReadPosition.At(StreamVersion.FromInt64(1));
        var b = StreamReadPosition.At(StreamVersion.FromInt64(1));

        a.Equals(b).ShouldBeTrue();
    }

    [Test]
    public void Equals_WhenValuesDiffer_ReturnsFalse()
    {
        var a = StreamReadPosition.At(StreamVersion.FromInt64(1));
        var b = StreamReadPosition.At(StreamVersion.FromInt64(2));

        a.Equals(b).ShouldBeFalse();
        StreamReadPosition.Start.Equals(StreamReadPosition.End).ShouldBeFalse();
        StreamReadPosition.Start.Equals(a).ShouldBeFalse();
        StreamReadPosition.End.Equals(a).ShouldBeFalse();
    }
}