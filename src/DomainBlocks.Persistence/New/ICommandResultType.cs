using System;

namespace DomainBlocks.Persistence.New;

public interface ICommandResultType
{
    public Type ClrType { get; }
}