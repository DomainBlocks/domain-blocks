using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    internal sealed class EventRoutes<TEventBase> : Dictionary<(Type, Type), ApplyEvent<object, TEventBase>>
    {
    }
}