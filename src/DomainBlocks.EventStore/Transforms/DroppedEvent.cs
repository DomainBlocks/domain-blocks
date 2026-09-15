namespace DomainBlocks.EventStore.Transforms;

/// <summary>
/// A ready-made placeholder for events dropped by a read transform, for stores whose event type is
/// <see cref="object"/>. It is emitted with the dropped event's context so that consumers still observe the event's
/// position; consumers ignore it. Stores over a narrower event type supply their own placeholder of that type.
/// </summary>
public sealed class DroppedEvent
{
    public static readonly DroppedEvent Instance = new();

    private DroppedEvent()
    {
    }

    public override string ToString() => nameof(DroppedEvent);
}