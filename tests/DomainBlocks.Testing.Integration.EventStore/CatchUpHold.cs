namespace DomainBlocks.Testing.Integration.EventStore;

public sealed class CatchUpHold(TaskCompletionSource tcs)
{
    public void Release() => tcs.SetResult();
}