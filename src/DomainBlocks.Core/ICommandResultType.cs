namespace DomainBlocks.Core;

public interface ICommandResultType
{
    Type ClrType { get; }
}