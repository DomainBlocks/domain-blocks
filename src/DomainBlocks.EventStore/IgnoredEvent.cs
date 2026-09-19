namespace DomainBlocks.EventStore;

public sealed class IgnoredEvent
{
    public static readonly IgnoredEvent Instance = new();

    private IgnoredEvent()
    {
    }

    public override string ToString() => nameof(IgnoredEvent);
}