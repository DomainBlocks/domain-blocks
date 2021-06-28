using System;
using System.ComponentModel.DataAnnotations;

namespace DomainLib.EventStore.AspNetCore
{
    public class EventStoreConnectionOptions
    {
        public const string ConfigSection = "EventStore";

        [Required]
        public Uri Uri { get; set; }
    }
}