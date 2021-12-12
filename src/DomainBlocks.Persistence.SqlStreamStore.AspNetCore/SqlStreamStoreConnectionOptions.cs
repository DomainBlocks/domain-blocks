using System;
using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.Persistence.SqlStreamStore.AspNetCore
{

    public class SqlStreamStoreConnectionOptions
    {
        public const string ConfigSection = "SqlStreamStore";

        [Required]
        public string ConnectionString { get; set; }
    }
}
