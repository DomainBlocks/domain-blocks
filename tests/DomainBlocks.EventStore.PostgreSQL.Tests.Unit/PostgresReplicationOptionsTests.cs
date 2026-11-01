using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresReplicationOptionsTests
{
    private static readonly TimeSpan[] OutOfRangeIntervals =
        [TimeSpan.Zero, TimeSpan.FromSeconds(-1), Timeout.InfiniteTimeSpan];

    private static readonly TimeSpan[] BoundaryIntervals = [TimeSpan.FromTicks(1), TimeSpan.MaxValue];

    private static readonly TimeSpan[] OutOfRangeDelays = [TimeSpan.FromTicks(-1), Timeout.InfiniteTimeSpan];

    private static readonly TimeSpan[] BoundaryDelays = [TimeSpan.Zero, TimeSpan.MaxValue];

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public void InactiveSlotTimeout_OutOfRange_Throws(TimeSpan timeout)
    {
        var options = new PostgresReplicationOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.InactiveSlotTimeout = timeout);
    }

    [TestCaseSource(nameof(BoundaryIntervals))]
    public void InactiveSlotTimeout_Boundary_IsSet(TimeSpan timeout)
    {
        var options = new PostgresReplicationOptions { InactiveSlotTimeout = timeout };

        options.InactiveSlotTimeout.ShouldBe(timeout);
    }

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public void WalReceiverTimeout_OutOfRange_Throws(TimeSpan timeout)
    {
        var options = new PostgresReplicationOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.WalReceiverTimeout = timeout);
    }

    [TestCaseSource(nameof(BoundaryIntervals))]
    public void WalReceiverTimeout_Boundary_IsSet(TimeSpan timeout)
    {
        var options = new PostgresReplicationOptions { WalReceiverTimeout = timeout };

        options.WalReceiverTimeout.ShouldBe(timeout);
    }

    [TestCaseSource(nameof(OutOfRangeIntervals))]
    public void WalReceiverStatusInterval_OutOfRange_Throws(TimeSpan interval)
    {
        var options = new PostgresReplicationOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.WalReceiverStatusInterval = interval);
    }

    [TestCaseSource(nameof(BoundaryIntervals))]
    public void WalReceiverStatusInterval_Boundary_IsSet(TimeSpan interval)
    {
        var options = new PostgresReplicationOptions { WalReceiverStatusInterval = interval };

        options.WalReceiverStatusInterval.ShouldBe(interval);
    }

    [TestCaseSource(nameof(OutOfRangeDelays))]
    public void RetryDelay_OutOfRange_Throws(TimeSpan delay)
    {
        var options = new PostgresReplicationOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.RetryDelay = delay);
    }

    [TestCaseSource(nameof(BoundaryDelays))]
    public void RetryDelay_Boundary_IsSet(TimeSpan delay)
    {
        var options = new PostgresReplicationOptions { RetryDelay = delay };

        options.RetryDelay.ShouldBe(delay);
    }

    [TestCaseSource(nameof(OutOfRangeDelays))]
    public void MaxRetryDelay_OutOfRange_Throws(TimeSpan delay)
    {
        var options = new PostgresReplicationOptions();

        Should.Throw<ArgumentOutOfRangeException>(() => options.MaxRetryDelay = delay);
    }

    [TestCaseSource(nameof(BoundaryDelays))]
    public void MaxRetryDelay_Boundary_IsSet(TimeSpan delay)
    {
        var options = new PostgresReplicationOptions { MaxRetryDelay = delay };

        options.MaxRetryDelay.ShouldBe(delay);
    }
}