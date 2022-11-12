namespace DomainBlocks.Projections.Sql.Tests
{
    public class UpsertDeleteEvent
    {
        public const string Name = "TestEvent5";
        
        public UpsertDeleteEvent(int id, int value)
        {
            Value = value;
            Id = id;
        }

        public int Id { get; }
        public int Value { get; }
    }
}