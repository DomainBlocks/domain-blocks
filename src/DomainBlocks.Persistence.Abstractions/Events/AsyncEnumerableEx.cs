namespace DomainBlocks.Persistence.Abstractions.Events;

internal static class AsyncEnumerableEx
{
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public static async IAsyncEnumerable<T> Empty<T>()
#pragma warning restore CS1998
    {
        yield break;
    }
}