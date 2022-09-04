using System;

namespace DomainBlocks.Core;

public interface ICommandReturnType
{
    public Type ClrType { get; }
}