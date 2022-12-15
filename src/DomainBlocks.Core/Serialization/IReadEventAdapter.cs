using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Core.Serialization;

public interface IReadEventAdapter<in TReadEvent>
{
    string GetEventName(TReadEvent readEvent);

    ValueTask<object> DeserializeEvent(
        TReadEvent readEvent,
        Type eventType,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> DeserializeMetadata(TReadEvent readEvent);
}