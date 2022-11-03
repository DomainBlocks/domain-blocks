using System;

namespace DomainBlocks.Core;

public interface ICommandResultOptions
{
    public Type ClrType { get; }
}