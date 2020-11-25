using System;

namespace DomainLib.EventStore.Testing
{
    public class TestAggregateState
    {
        public TestAggregateState(Guid id, int totalNumber)
        {
            Id = id;
            TotalNumber = totalNumber;
        }
        
        public Guid Id { get; }
        public int TotalNumber { get; }
    }
}