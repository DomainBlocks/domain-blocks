using System;

namespace DomainBlocks.EventStore.Testing;

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