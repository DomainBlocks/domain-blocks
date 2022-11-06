using System;

namespace DomainBlocks.Core;

public interface ICommandResultOptions
{
    Type ClrType { get; }
}