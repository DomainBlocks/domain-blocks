using System.ComponentModel.DataAnnotations;

namespace DomainLib.EventStore.Common.AspNetCore
{
    public class EventStoreConnectionOptions
    {
        public const string ConfigSection = "EventStore";

        [Required]
        public string ConnectionString { get; set; }
    }
}