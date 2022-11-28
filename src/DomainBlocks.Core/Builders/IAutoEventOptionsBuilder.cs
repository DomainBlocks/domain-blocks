using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

internal interface IAutoEventOptionsBuilder<TAggregate>
{
    IEnumerable<EventOptions<TAggregate>> Build();
}