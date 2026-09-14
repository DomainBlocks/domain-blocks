using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

internal static class NpgsqlDbTypeExtensions
{
    extension(NpgsqlDbType elementType)
    {
        // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
        public NpgsqlDbType AsArray() => NpgsqlDbType.Array | elementType;
    }
}