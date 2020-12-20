namespace DomainLib.Projections.Sql.Tests
{
    public class DeleteCustomSqlEvent
    {
        public const string Name = "TestEvent4";
        public const string CustomSqlText = "Some custom SQL";

        public DeleteCustomSqlEvent(int id, int value)
        {
            Value = value;
            Id = id;
        }

        public int Id { get; }
        public int Value { get; }
    }
}