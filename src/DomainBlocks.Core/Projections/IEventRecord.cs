namespace DomainBlocks.Core.Projections;

public interface IEventRecord
{
    object Event { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}

public interface IEventRecord<out TEvent> : IEventRecord
{
    new TEvent Event { get; }
}