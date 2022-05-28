namespace DomainBlocks.Aggregates
{
    public sealed class CommandRegistrations<TCommandBase, TEventBase>
    {
        public CommandRoutes<TCommandBase, TEventBase> Routes { get; } = new();
        public ImmutableCommandRoutes<TCommandBase, TEventBase> ImmutableRoutes { get; } = new();
    }
}