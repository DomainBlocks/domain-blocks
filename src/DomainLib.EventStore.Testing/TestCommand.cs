namespace DomainLib.EventStore.Testing
{
    public class TestCommand
    {
        public TestCommand(int number)
        {
            Number = number;
        }

        public int Number { get; }
    }
}