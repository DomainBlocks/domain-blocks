using System;

namespace DomainLib.Projections.Sqlite.Tests.Events
{
    public class ShoppingCartCreated
    {
        public const string EventName = "ShoppingCartCreated";

        public ShoppingCartCreated(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }
    }
}