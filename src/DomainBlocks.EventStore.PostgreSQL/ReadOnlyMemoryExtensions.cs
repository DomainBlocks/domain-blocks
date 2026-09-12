using System.Runtime.InteropServices;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class ReadOnlyMemoryExtensions
{
    extension(ReadOnlyMemory<byte> bytes)
    {
        /// <summary>
        /// Returns the underlying array when the memory covers a whole array, otherwise a copy. The result may alias
        /// the caller's buffer, so it must only be read from.
        /// </summary>
        public byte[] GetArrayOrCopy()
        {
            return MemoryMarshal.TryGetArray(bytes, out var segment) &&
                   segment is { Offset: 0, Array: { } wholeArray } &&
                   wholeArray.Length == segment.Count
                ? wholeArray
                : bytes.ToArray();
        }
    }
}