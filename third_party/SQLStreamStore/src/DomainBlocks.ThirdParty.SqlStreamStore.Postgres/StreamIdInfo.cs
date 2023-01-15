using DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Postgres
{
    internal struct StreamIdInfo // Love this name
    {
        public readonly PostgresqlStreamId PostgresqlStreamId;

        public readonly PostgresqlStreamId MetadataPosgresqlStreamId;

        public StreamIdInfo(string idOriginal)
        {
            Ensure.That(idOriginal, nameof(idOriginal)).IsNotNullOrWhiteSpace();

            PostgresqlStreamId = new PostgresqlStreamId(idOriginal);
            MetadataPosgresqlStreamId = new PostgresqlStreamId("$$" + idOriginal);
        }
    }
}