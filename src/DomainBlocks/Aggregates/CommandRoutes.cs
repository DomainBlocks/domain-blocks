using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    internal sealed class CommandRoutes<TCommandBase, TEventBase>
        : Dictionary<(Type, Type), ExecuteCommand<object, TCommandBase, TEventBase>>
    {
    }
}