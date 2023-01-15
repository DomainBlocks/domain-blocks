namespace DomainBlocks.Core.Process;

public sealed class ProcessTransitionResult<TProcess>
{
    public ProcessTransitionResult(IEnumerable<object> commands, TProcess updatedState)
    {
        Commands = commands;
        UpdatedState = updatedState;
    }

    public IEnumerable<object> Commands { get; }
    public TProcess UpdatedState { get; }
}