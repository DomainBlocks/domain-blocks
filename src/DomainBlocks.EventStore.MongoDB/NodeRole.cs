namespace DomainBlocks.EventStore.MongoDB;

[Flags]
public enum NodeRole
{
    None = 0,
    Client = 1,
    Leader = 2,
    ClientLeader = Client | Leader
}