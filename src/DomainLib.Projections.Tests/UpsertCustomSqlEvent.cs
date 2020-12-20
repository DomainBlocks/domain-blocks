namespace DomainLib.Projections.Sql.Tests
{
    public class UpsertCustomSqlEvent
    {
        public const string Name = "TestEvent3";
        public const string CustomSqlText = "Some custom SQL";

        public UpsertCustomSqlEvent(int id, int value)
        {
            Value = value;
            Id = id;
        }

        public int Id { get; }
        public int Value { get; }
    }
}