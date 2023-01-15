#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That.Extensions
{
    internal static class StringExtensions
    {
        internal static string Inject(this string format, params object[] formattingArgs)
        {
            return string.Format(format, formattingArgs);
        }

        internal static string Inject(this string format, params string[] formattingArgs)
        {
            return string.Format(format, formattingArgs.Select(a => a as object).ToArray());
        }
    }
}