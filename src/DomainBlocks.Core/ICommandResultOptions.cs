using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface ICommandResultOptions
{
    public Type ClrType { get; }
}

public interface ICommandResultOptions<TCommandResult> : ICommandResultOptions
{
    public TCommandResult Coerce(TCommandResult commandResult, IEnumerable<object> raisedEvents);
}