using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    public sealed class ImmutableCommandRoutes<TCommandBase, TEventBase>
        : Dictionary<(Type, Type), ImmutableExecuteCommand<object, TCommandBase, TEventBase>>
    {
    }
}