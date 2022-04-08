using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.SqlStreamStore.Common.AspNetCore
{

    public class SqlStreamStoreConnectionOptions
    {
        public const string ConfigSection = "SqlStreamStore";

        [Required]
        public string ConnectionString { get; set; }

        public string SchemaName { get; set; }
    }
}
