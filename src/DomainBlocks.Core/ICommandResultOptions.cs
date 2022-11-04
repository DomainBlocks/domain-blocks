using System;
using System.Collections.Generic;

namespace DomainBlocks.Core;

public interface ICommandResultOptions
{
    Type ClrType { get; }
}

public interface ICommandResultOptions<TCommandResult> : ICommandResultOptions
{
    TCommandResult Coerce(TCommandResult commandResult, IEnumerable<object> raisedEvents);
}