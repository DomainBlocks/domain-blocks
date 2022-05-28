using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    public sealed class EventRoutes<TEventBase> : Dictionary<(Type, Type), ApplyEvent<object, TEventBase>>
    {
    }
}