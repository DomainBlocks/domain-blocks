namespace DomainBlocks.Experimental.EventSourcing.Extensions;

public static class EnumerableExtensions
{
    public static IEnumerable<T> Clone<T>(this IEnumerable<IDeepCloneable<T>> source)
    {
        return source.Select(x => x.Clone());
    }
}