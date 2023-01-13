namespace DomainBlocks.EventStore.Testing;

public class TestEvent
{
    public TestEvent(int number)
    {
        Number = number;
    }

    public int Number { get; }
}