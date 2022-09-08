using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IEventTypeBuilder
{
    public IEnumerable<IEventType> Build();
}