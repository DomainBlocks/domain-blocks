using System.Collections.Generic;

namespace DomainBlocks.Projections.New;

public interface IEventRecord<out TEvent>
{
    TEvent Event { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}