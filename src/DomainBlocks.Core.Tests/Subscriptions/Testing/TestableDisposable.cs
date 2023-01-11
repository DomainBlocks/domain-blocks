namespace DomainBlocks.Core.Tests.Subscriptions.Testing;

public class TestableDisposable : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}