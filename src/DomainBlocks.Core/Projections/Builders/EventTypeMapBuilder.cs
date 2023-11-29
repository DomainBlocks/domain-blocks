namespace DomainBlocks.Core.Projections.Builders;

public sealed class EventTypeMapBuilder
{
    internal ProjectionEventTypeMap EventTypeMap { get; set; } = new();

    internal EventTypeMapBuilder()
    {
    }

    public EventNameToTypeBuilder EventName(string eventName) => new(this, eventName);
}