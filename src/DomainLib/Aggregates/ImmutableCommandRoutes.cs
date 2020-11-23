using System;
using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    internal sealed class ImmutableCommandRoutes<TCommandBase, TEventBase>
        : Dictionary<(Type, Type), ImmutableExecuteCommand<object, TCommandBase, TEventBase>>
    {
    }
}