using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    public sealed class ImmutableEventRoutes<TEventBase>
        : Dictionary<(Type, Type), ImmutableApplyEvent<object, TEventBase>>
    {
    }
}