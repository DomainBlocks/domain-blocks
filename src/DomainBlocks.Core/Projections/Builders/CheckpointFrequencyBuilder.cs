using DomainBlocks.Core.Subscriptions;

namespace DomainBlocks.Core.Projections.Builders;

public sealed class CheckpointFrequencyBuilder
{
    private int? _eventCount;
    private TimeSpan? _timeInterval;

    internal CheckpointFrequencyBuilder()
    {
    }

    public CheckpointFrequencyBuilder PerEventCount(int eventCount)
    {
        _eventCount = eventCount;
        return this;
    }

    public CheckpointFrequencyBuilder Or() => this;

    public void PerTimeInterval(TimeSpan timeInterval)
    {
        _timeInterval = timeInterval;
    }

    internal CheckpointFrequency Build() => new()
    {
        PerEventCount = _eventCount,
        PerTimeInterval = _timeInterval
    };
}