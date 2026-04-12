using System.Security.Cryptography;
using System.Text;

namespace DomainBlocks.EventStore.Primitives.Identity;

public static class GuidExtensions
{
    extension(Guid)
    {
        /// <summary>
        /// Generates a deterministic UUID version 5 (SHA-1 name-based) value as defined by RFC 4122 and the UUIDv5
        /// specification.
        /// </summary>
        /// <param name="namespace">The UUID namespace identifier.</param>
        /// <param name="name">The name from which the UUID is deterministically derived.</param>
        /// <returns>A UUID version 5 value that is stable for the same namespace and name.</returns>
        public static Guid CreateVersion5(Guid @namespace, string name)
        {
            // UUIDv5 = SHA-1(namespace_octets || name_octets), then take leftmost 128 bits, then set version=5 and
            // variant=RFC4122.

            // --- Step 1: Convert Namespace UUID to canonical octet sequence (network byte order) ---

            // Allocate 16 bytes for the namespace UUID (UUIDs are 128 bits / 16 octets).
            Span<byte> ns = stackalloc byte[16];

            // Write the .NET Guid bytes into 'ns'.
            // Note: .NET Guid byte layout is not RFC 4122 "network byte order" for the first 3 fields.
            if (!@namespace.TryWriteBytes(ns))
                throw new InvalidOperationException("Failed to write namespace GUID bytes.");

            // Convert .NET Guid layout -> RFC 4122 / network byte order before hashing.
            SwapGuidByteOrder(ns);

            // --- Step 2: Convert the name to its canonical octet sequence ---

            // Name must be converted to a canonical sequence of octets per the namespace conventions.
            // Common/typical convention: UTF-8 bytes for the name string.
            var nameBytes = Encoding.UTF8.GetBytes(name);

            // --- Step 3: Compute SHA-1(namespace_octets || name_octets) ---

            // SHA-1 produces 160 bits = 20 bytes.
            Span<byte> sha1 = stackalloc byte[20];

            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                // SHA-1 hash over Namespace ID concatenated with the desired name
                // Concatenation order matters: namespace first, then name.
                hash.AppendData(ns);
                hash.AppendData(nameBytes);

                // Finalize the SHA-1 digest into 'sha1'.
                if (!hash.TryGetHashAndReset(sha1, out var written) || written != 20)
                    throw new InvalidOperationException("Failed to compute SHA1 hash.");
            }

            // --- Step 4: Use the most significant (leftmost) 128 bits of SHA-1 output ---

            // Leftmost 128 bits = first 16 bytes of the SHA-1 digest.
            Span<byte> uuid = stackalloc byte[16];
            sha1[..16].CopyTo(uuid);

            // At this point, 'uuid' is 16 bytes taken from SHA-1 in RFC/network byte order.

            // --- Step 5: Set the UUID version field to 5 ---

            // Version field occupies bits 48-51, i.e. the high nibble of octet 6 (uuid[6]) in RFC 4122 order.
            //
            // Clear the high nibble (keep low nibble from the hash), then OR in 0x5 << 4 (0x50).
            // 0x0F = 0000 1111 (mask to keep low nibble)
            // 0x50 = 0101 0000 (sets version to 0101)
            uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);

            // --- Step 6: Set the UUID variant field to RFC 4122 (10xx xxxx) ---

            // Variant occupies bits 64-65, i.e. the two most significant bits of octet 8 (uuid[8]).
            // RFC 4122 variant requires the pattern 10xxxxxx.
            //
            // Clear the top two bits (keep lower 6 bits from the hash), then set the top bit to 1 and next to 0.
            // 0x3F = 0011 1111 (clears top two bits)
            // 0x80 = 1000 0000 (sets top bit, leaving next bit 0 => 10xxxxxx)
            uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);

            // --- Step 7: Convert RFC/network byte order back to .NET Guid layout ---

            // Not a UUIDv5 spec requirement, but required for correct construction of a .NET Guid from bytes.
            // .NET Guid expects its own byte layout (little-endian for first 3 fields).
            SwapGuidByteOrder(uuid);

            // Return the final UUIDv5 value as a Guid.
            return new Guid(uuid);
        }
    }

    /// <summary>
    /// Converts between .NET Guid byte layout and RFC 4122 network byte order. Applying it twice returns the original.
    ///
    /// RFC 4122 fields:
    /// - time_low (4 bytes)
    /// - time_mid (2 bytes)
    /// - time_hi_and_version (2 bytes)
    /// are interpreted big-endian in the canonical/network byte sequence.
    ///
    /// .NET Guid byte array layout stores these three fields little-endian, so we reverse each of those regions to
    /// convert between the two.
    /// </summary>
    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        // Reverse time_low (bytes 0-3)
        bytes[..4].Reverse();

        // Reverse time_mid (bytes 4-5)
        bytes.Slice(4, 2).Reverse();

        // Reverse time_hi_and_version (bytes 6-7)
        bytes.Slice(6, 2).Reverse();

        // Bytes 8-15 match RFC 4122 order already and are not reversed.
    }
}