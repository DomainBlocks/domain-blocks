namespace DomainLib.Projections.Sql.Tests
{
    public class MultipleNamesEvent
    {
        public const string Name = "TestEvent2";
        public const string OtherName = "TestEvent2_OtherName";

        public MultipleNamesEvent(int id, string data)
        {
            Data = data;
            Id = id;
        }

        public int Id { get; }
        public string Data { get; }
    }
}