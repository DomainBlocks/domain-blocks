using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Core.Serialization;

public interface IReadEventConverter<in TReadEvent>
{
    ValueTask<object> DeserializeEvent(
        TReadEvent readEvent,
        Type eventTypeOverride = null,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> DeserializeMetadata(TReadEvent readEvent);
}