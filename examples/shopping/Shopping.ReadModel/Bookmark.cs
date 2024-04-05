namespace Shopping.ReadModel;

public class Bookmark
{
    public const int DefaultId = 1;
    public int Id { get; set; } = DefaultId;
    public ulong Position { get; set; }
}