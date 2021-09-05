using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    internal sealed class ImmutableEventRoutes<TEventBase>
        : Dictionary<(Type, Type), ImmutableApplyEvent<object, TEventBase>>
    {
    }
}