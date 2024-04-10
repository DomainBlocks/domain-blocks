using System.ComponentModel.DataAnnotations;

namespace DomainBlocks.EventStore;

public class EventStoreConnectionOptions
{
    public const string ConfigSection = "EventStore";

    [Required]
    public string ConnectionString { get; set; } = null!;
}