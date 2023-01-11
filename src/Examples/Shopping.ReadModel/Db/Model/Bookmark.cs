namespace Shopping.ReadModel.Db.Model;

public class Bookmark
{
    public const int DefaultId = 1;
    public int Id { get; set; } = DefaultId;
    public long Position { get; set; }
}