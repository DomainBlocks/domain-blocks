using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    internal sealed class ImmutableCommandRoutes<TCommandBase, TEventBase>
        : Dictionary<(Type, Type), ImmutableExecuteCommand<object, TCommandBase, TEventBase>>
    {
    }
}