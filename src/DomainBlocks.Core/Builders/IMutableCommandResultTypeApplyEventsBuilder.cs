namespace DomainBlocks.Core.Builders;

public interface IMutableCommandResultTypeApplyEventsBuilder
{
    /// <summary>
    /// Specify to update the aggregate's state by applying the returned events. Use this option when the mutable
    /// aggregate's state is not updated by the command method.
    /// </summary>
    void ApplyEvents();
}