namespace Shopping.ReadModel;

public class Bookmark
{
    public const int DefaultId = 1;
    public int Id { get; set; } = DefaultId;
    public long Position { get; set; }
    public ulong CommitPosition { get; set; }
    public ulong PreparePosition { get; set; }
}