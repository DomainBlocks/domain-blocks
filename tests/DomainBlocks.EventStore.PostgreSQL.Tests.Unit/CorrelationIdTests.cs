using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class CorrelationIdTests
{
    [Test]
    public void TryReserve_WhenIdIsActive_RejectsDuplicateUntilReleased()
    {
        CorrelationId.TryReserve("orders").ShouldBeTrue();
        CorrelationId.TryReserve("orders").ShouldBeFalse();
        CorrelationId.Release("orders");
        CorrelationId.TryReserve("orders").ShouldBeTrue();
        CorrelationId.Release("orders");
    }

    [Test]
    public void ReserveGenerated_ReturnsAnActiveShortId()
    {
        var id = CorrelationId.ReserveGenerated();

        id.Length.ShouldBe(CorrelationId.IdLength);
        CorrelationId.TryReserve(id).ShouldBeFalse();
        CorrelationId.Release(id);
    }
}
