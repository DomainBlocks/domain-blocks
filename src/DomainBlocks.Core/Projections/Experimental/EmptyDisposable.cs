namespace DomainBlocks.Core.Projections.Experimental;

internal sealed class EmptyDisposable : IDisposable
{
    public static readonly IDisposable Instance = new EmptyDisposable();

    private EmptyDisposable()
    {
    }

    public void Dispose()
    {
    }
}