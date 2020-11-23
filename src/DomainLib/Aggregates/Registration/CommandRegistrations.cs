using System;

namespace DomainLib.Aggregates.Registration
{
    internal sealed class CommandRegistrations<TCommandBase, TEventBase>
    {
        public CommandRoutes<TCommandBase, TEventBase> Routes { get; } = new();
        public ImmutableCommandRoutes<TCommandBase, TEventBase> ImmutableRoutes { get; } = new();
        public Action<TCommandBase> PreCommandHook { get; internal set; }
        public Action<TCommandBase> PostCommandHook { get; internal set; }
    }
}