namespace DomainBlocks.MongoDB.Sequencing;

public class AppendOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}