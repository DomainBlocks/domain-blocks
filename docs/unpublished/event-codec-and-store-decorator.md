# Event codec and store decorator

September 2026. Records the shape of the event translation layers after the codec refactor, the reasoning behind
where each concern lives, and the before/after measurements.

## The rule

**Wire concerns live in the codec; domain policy lives around the store.**

```
EventSourcedStateStore / projections                agnostic
────────────────────────────────────────────────────────────────────────────
EventStoreDecorator (internal)                      DomainBlocks.EventStore
  WithMetadataContributors: contributors on append, pooled chunks per batch
  WithReadTransforms:       transforms on reads and subscriptions, 1→N, context preserved
────────────────────────────────────────────────────────────────────────────
Store (PostgreSQL / MongoDB / KurrentDB)            takes IEventCodec<TEvent, TData, TMetadata>
────────────────────────────────────────────────────────────────────────────
EventCodec = EventTypeMap + serializers + contract mappers      DomainBlocks.EventStore.Codecs
IEventEncoder / IEventDecoder / IEventCodec                     DomainBlocks.EventStore.Codecs
────────────────────────────────────────────────────────────────────────────
IObjectSerializer<TData> / IMetadataSerializer<TData>            DomainBlocks.Serialization.Abstractions
```

## Serialization

One interface per kind, both directions: `IObjectSerializer<TData>` and `IMetadataSerializer<TData>`. Every
implementation implemented both halves already, so the former serializer/deserializer/serde split only added reading
cost. `IByteObjectSerializer` and `IByteMetadataSerializer` let one class serve `byte[]` and `ReadOnlyMemory<byte>`:
`Serialize` returns a byte array, `Deserialize` goes through a span overload.

Metadata `Serialize` takes `ReadOnlySpan<KeyValuePair<string, string>>`, so the write path never builds a
dictionary. The JSON serializers write the object with a thread-cached `Utf8JsonWriter`; keys are written as given
(`DictionaryKeyPolicy` does not apply to metadata).

## Codec

`IEventCodec<TEvent, TData, TMetadata>` is single-event and symmetric:

```csharp
EncodedEvent<TData, TMetadata> Encode(TEvent payload, ReadOnlySpan<KeyValuePair<string, string>> metadata);
DecodedEvent<TEvent> Decode(string eventName, TData data, TMetadata? metadata);
```

The encoder and decoder halves remain separate interfaces beneath it because decode-only consumers exist (the
PostgreSQL log reader and replication session, and tests that substitute a raw decoder). Stores accept `IEventCodec`
and hand their internal readers the decoder half. `EventEncoderExtensions.Encode(IEnumerable<AppendableEvent>)`
encodes a batch lazily for stores that stream.

`EventCodec` is the default and only implementation in the library. It resolves stored names through
`EventTypeMap`, maps domain events to their wire contracts where an `IEventContractMapper` is registered, and
serializes. Contract mapping is a codec concern because it decides which type is serialized and therefore which name
is written; it is a few lines and lives inside rather than as a decorator over an object-typed inner codec that
nothing else would want. `Encode` takes already-merged metadata, so the codec owns no buffers and no policy.

Metadata that is absent, or excluded by a read, arrives as `default` (`null` for PostgreSQL text, `default` memory
for KurrentDB) and decodes to `FrozenDictionary<string, string>.Empty`. MongoDB projects the field out, which
surfaces as `BsonNull`; the BSON metadata serializer handles that itself.

## Store decorator

```csharp
var store = PostgresEventStore.Create(dataSource, codec)
    .WithMetadataContributors(new CorrelationContributor())
    .WithReadTransforms(new ShipmentDispatchedTransform());
```

These are the primitives. The usual way in is a store builder, which applies the same hooks at `Build()` and returns
the concrete store; see [event-store-creation.md](event-store-creation.md).

Two extension methods on `IEventStore`, one per hook, each returning `IEventStore`. There is no umbrella noun: the
hooks are a fixed pair, not a user-ordered chain, so "pipeline" or "middleware" would over-promise. Both are backed
by one internal `EventStoreDecorator`; calling either on an already decorated store rebuilds that single decorator
with the merged configuration rather than stacking a second layer. With nothing to add, each method returns the
store itself, and a decorator with only contributors returns the inner store's read enumerables untouched. The
decorator forwards disposal to the store; `IEventStore` itself extends `IAsyncDisposable`.

### Metadata contributors

```csharp
public interface IMetadataContributor<in TEvent> { void Contribute(TEvent @event, MetadataWriter metadata); }
```

Contributors run in order for every event. The merge happens in pooled 4 KB chunks shared by the batch: each
event gets a slice of one chunk, keys are de-duplicated within the slice by a linear scan, and explicit
`AppendableEvent` metadata is overlaid last so it wins. `AppendableEvent` holds its metadata as `ReadOnlyMemory` so
events share a chunk without copying; its `Metadata` span is unchanged. The hook is lazy, so a store that streams
its input keeps streaming.

The chunks go back to `ArrayPool` when the inner append completes, which is the point at which the store has
consumed every event (it cannot append what it has not encoded). So the events the decorator hands a store are valid
for the duration of the append only, and a warmed-up batch allocates nothing for the merge. The first version used
one growing array per batch instead; it retained 16 bytes per entry for the whole batch and its doubling landed on
the large object heap, which showed up in the benchmark as +20 % allocated and Gen2 collections. Deferring the
merge to the store layer is only free if the storage is recycled at the batch boundary.

The previous contributor signature also passed the contract and wire name. Nothing used them and they tied domain
policy to the codec, so they are gone; a contributor that needs the wire name can hold the `EventTypeMap`.

### Read transforms

```csharp
public interface IReadEventTransform<TEvent>
{
    Type SourceEventType { get; }
    IEnumerable<TEvent> Apply<TStreamId, TStreamPos, TLogPos>(
        TEvent @event, in ReadEventContext<TStreamId, TStreamPos, TLogPos> context);
}

// common case: no positions needed
public abstract class ReadEventTransform<TEventBase, TSourceEvent> : IReadEventTransform<TEventBase>
{
    protected abstract IEnumerable<TEventBase> Apply(TSourceEvent @event, ReadEventInfo info);
}
```

* Applied to `ReadStream`, `ReadAll`, `SubscribeToAll` and `SubscribeToStream`, so loads and projections see the
  same events. Previously transforms were an extension over `IAsyncEnumerable` that each caller had to remember.
* Derived events keep the source event's `ReadEventContext`. `EventSourcedStateStore` keeps recording the last
  observed `StreamPosition` and never counts events, so a 1→N fan-out needs no change there.
* Derived events are run through the transforms again, depth first, so a chained upcast keeps its place before its
  siblings. A transform that yields its own source type, or a chain deeper than 32, throws.
* Untransformed events and non-event subscription messages pass through by reference. The lookup table is built once
  in the decorator, not per enumeration.
* **A transform that yields nothing throws** unless a placeholder is configured with
  `WithReadTransforms(transforms, droppedEventPlaceholder: ...)`. With one, the placeholder is emitted in the
  dropped event's place, carrying its context, so consumers still observe the position and need only ignore that
  one type. `DroppedEvent.Instance` serves stores over `object`; a store over a narrower event type supplies its own.
  The reason drops are never silent: a dropped tail event would make `EventSourcedStateStore` under-report the
  stream version on every load, so every save would conflict, and dropping every event would make
  `LoadRequiredAsync` throw for an existing stream. The placeholder moves the "ignore this" decision from every
  consumer knowing every retired type to every consumer ignoring one type.

### Subscriptions and 1→N

All events derived from one source share its `LogPosition`. A consumer that checkpoints after the first derived
event and crashes before the second resumes *after* the source and never sees the second. Treat a transform group
as one delivery unit: checkpoint after the last event of a position, not after each event. If finer checkpoints are
ever needed, an ordinal or last-in-group flag on `ReadEventContext` is the follow-up.

## Alternatives rejected

* Keeping policy in the codec and calling a transform helper from each store's read paths: 12+ call sites across
  three store projects plus the replication feed, and stores gain a dependency on domain policy.
* One "pipeline" object replacing the codec in every store `Create`: the same call-site problem, under a new name.
* A `WithPipeline(p => p.ContributeMetadata(...).Transform(...))` builder: the first shape of this decorator.
  Dropped because "pipeline" suggests an ordered, extensible chain and there are exactly two fixed hooks.
* Contract mapping as a codec decorator over `IEventCodec<object, …>`: changes the type parameter, so it is an
  adapter dressed as a decorator, and the object-typed inner codec is a type nothing else wants.
* Metadata contribution as a codec or encoder decorator with a thread-static dictionary: zero-allocation, but puts
  append policy in the wire layer. The pooled buffer in the store decorator is also zero-allocation per event.
* A merged `KeyValuePair[]` per event in the store decorator: one allocation per event.
* An `allowDroppingEvents` flag on the read transforms: permitted a mode in which a dropped tail event silently
  lost its position. Replaced by the placeholder, which has no such mode.
* Emitting the placeholder only when a position would otherwise be lost (holding it back until the next event or a
  `CaughtUp` shows it is not needed): precise, but makes the output depend on what follows. The always-emit rule is
  deterministic and costs one struct copy of a shared instance per dropped event.

## Benchmarks

`benchmarks/DomainBlocks.EventStore.Benchmarks`, no I/O, 10 000 events per operation, `[MemoryDiagnoser]`:

```
dotnet run -c Release --project benchmarks/DomainBlocks.EventStore.Benchmarks -- --filter '*' --join
```

The scenarios were added before the refactor so the same code paths are compared:

* Write, `WithMetadata=false`: bare encode. `true`: one contributor plus one explicit entry per event.
* Read, `IncludeMetadata` × `Transforms`: `None` plain decode; `Probe` a transform registered for a type that never
  occurs, so only the per-event lookup is paid; `FanOut` every event becomes two.

Measured on 2026-09-15 (AMD Ryzen AI MAX+ 395, .NET 10.0.1, x64 RyuJIT). Before is the first commit of this
branch, the baseline with the extended scenarios on the old code; after is the head of the refactor.

| Scenario | Mean before | Mean after | Change | Allocated before | Allocated after | Change |
|---|---:|---:|---:|---:|---:|---:|
| Write, WithMetadata=False | 889.6 μs | 868.7 μs | -2.3% | 703.34 KB | 703.26 KB | 0.0% |
| Write, WithMetadata=True | 2,493.7 μs | 1,956.9 μs | -21.5% | 2421.44 KB | 1876.63 KB | -22.5% |
| Read, IncludeMetadata=False, Transforms=None | 1,329.0 μs | 1,338.2 μs | +0.7% | 1873.63 KB | 1873.63 KB | 0.0% |
| Read, IncludeMetadata=False, Transforms=Probe | 1,668.2 μs | 1,584.2 μs | -5.0% | 1874.55 KB | 1873.95 KB | 0.0% |
| Read, IncludeMetadata=False, Transforms=FanOut | 2,501.4 μs | 2,631.6 μs | +5.2% | 3280.8 KB | 3280.51 KB | 0.0% |
| Read, IncludeMetadata=True, Transforms=None | 2,678.0 μs | 2,623.1 μs | -2.1% | 5700.19 KB | 5700.19 KB | 0.0% |
| Read, IncludeMetadata=True, Transforms=Probe | 3,100.1 μs | 3,078.4 μs | -0.7% | 5701.12 KB | 5700.52 KB | 0.0% |
| Read, IncludeMetadata=True, Transforms=FanOut | 3,911.4 μs | 4,195.7 μs | +7.3% | 7107.37 KB | 7107.07 KB | 0.0% |

Reading the table:

* **Write with metadata is 22 % cheaper in both time and bytes.** The dictionary is gone from the write path
  (linear-scan merge into pooled chunks, span-based JSON writer), and the old per-batch dictionary plus its
  enumerator during serialization are gone with it. The bare encode path is unchanged, as expected.
* **Decode is unchanged**, byte for byte.
* **Probe is 1–5 % faster**: the transform lookup table is built once instead of per enumeration.
* **FanOut is 5–7 % slower in time with identical allocations.** Every event goes through the generic-method
  `Apply`, the `ReadEventInfo` construction and the depth-first expansion into a reused list, where the old code
  made a plain interface call and a queue round-trip. That is roughly 13 ns per derived event on this machine and
  only paid by events that are actually transformed, so it was accepted in exchange for one type parameter on
  transforms and order-correct chaining. If it matters later, the generic `Apply` can be specialised per store
  position type.
