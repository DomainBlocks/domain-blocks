namespace DomainLib.Projections.Sql.Tests
{
    public class TestEvent
    {
        public const string Name = "TestEvent1";

        public TestEvent(int id, int value)
        {
            Value = value;
            Id = id;
        }

        public int Id { get; }
        public int Value { get; }
    }
}