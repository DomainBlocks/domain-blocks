using System.Runtime.InteropServices;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class ReadOnlyMemoryExtensions
{
    extension(ReadOnlyMemory<byte> bytes)
    {
        /// <summary>
        /// Returns the underlying array if the memory spans all of it, and otherwise a copy. The result may be the
        /// caller's own buffer, so it must only be read.
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