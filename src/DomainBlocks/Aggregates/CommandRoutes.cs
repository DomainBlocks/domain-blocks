using System;
using System.Collections.Generic;

namespace DomainBlocks.Aggregates
{
    public sealed class CommandRoutes<TCommandBase, TEventBase>
        : Dictionary<(Type, Type), ExecuteCommand<object, TCommandBase, TEventBase>>
    {
    }
}