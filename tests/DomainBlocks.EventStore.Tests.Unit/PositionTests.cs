using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit;

public class PositionTests
{
    private static readonly ReadEventContext<string, StreamPosition, LogPosition> Context = ReadEventContext.Create(
        "stream-1",
        "OrderPlaced",
        DateTimeOffset.UnixEpoch,
        StreamPosition.FromInt64(3),
        LogPosition.FromInt64(7));

    [Test]
    public void LogPositionFromContext_EventContext_ReturnsLogPosition()
    {
        LogPosition.FromContext(Context).ShouldBe(LogPosition.FromInt64(7));
    }

    [Test]
    public void StreamPositionFromContext_EventContext_ReturnsStreamPosition()
    {
        StreamPosition.FromContext(Context).ShouldBe(StreamPosition.FromInt64(3));
    }
}