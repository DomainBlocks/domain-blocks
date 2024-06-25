namespace Shopping.ReadModel.Db;

public class Checkpoint
{
    public string Name { get; set; } = null!;
    public ulong Position { get; set; }
}