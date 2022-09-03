using System;

namespace DomainBlocks.Persistence.New;

public interface ICommandReturnType
{
    public Type ClrType { get; }
}