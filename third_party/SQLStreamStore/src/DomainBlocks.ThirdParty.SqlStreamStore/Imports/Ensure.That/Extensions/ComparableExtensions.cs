#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That.Extensions
{
    internal static class ComparableExtensions
    {
        internal static bool IsLt<T>(this IComparable<T> x, T y)
        {
            return x.CompareTo(y) < 0;
        }

        internal static bool IsEq<T>(this IComparable<T> x, T y)
        {
            return x.CompareTo(y) == 0;
        }

        internal static bool IsGt<T>(this IComparable<T> x, T y)
        {
            return x.CompareTo(y) > 0;
        }
    }
}