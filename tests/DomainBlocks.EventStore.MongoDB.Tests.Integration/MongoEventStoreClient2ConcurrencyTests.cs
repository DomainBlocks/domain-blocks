// using DomainBlocks.EventStore.Abstractions;
// using DomainBlocks.EventStore.TypeMapping;
// using DomainBlocks.Testing.Integration.MongoDB;
// using MongoDB.Bson;
// using MongoDB.Driver;
// using NUnit.Framework;
// using Shouldly;
//
// namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;
//
// /// <summary>
// /// Validates correctness of <see cref="MongoEventStoreClient2"/> under concurrent multi-writer load.
// /// Each <see cref="MongoEventStoreClient2"/> instance represents an independent writer process
// /// sharing the same MongoDB database.
// /// </summary>
// [TestFixture]
// public class MongoEventStoreClient2ConcurrencyTests
// {
//     private const int WriterCount = 20;
//
// #if DEBUG
//     private const int TestTimeoutMillis = 10 * 60 * 1_000;
// #else
//     private const int TestTimeoutMillis = 60_000;
// #endif
//
//     private MongoClient _mongoClient = null!;
//     private MongoEventStoreClientOptions2 _options = null!;
//     private List<MongoEventStoreClient2<IDomainEvent>> _writers = null!;
//
//     [OneTimeSetUp]
//     public async Task OneTimeSetUp()
//     {
//         _mongoClient = new MongoClient(TestMongoConnectionStrings.Default);
//
//         _options = new MongoEventStoreClientOptions2
//         {
//             DatabaseName = "domainblocks_tests_v2_concurrency"
//         };
//
//         await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, _options);
//     }
//
//     [SetUp]
//     public void SetUp()
//     {
//         var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());
//         var eventCodec = TestMongoEventCodec.Create<IDomainEvent>(eventTypeMap);
//
//         _writers = Enumerable
//             .Range(0, WriterCount)
//             .Select(_ => new MongoEventStoreClient2<IDomainEvent>(_mongoClient, eventCodec, _options))
//             .ToList();
//     }
//
//     [TearDown]
//     public async Task TearDown()
//     {
//         foreach (var writer in _writers)
//             await writer.DisposeAsync();
//
//         // Drop the event log and sequence collections between tests for isolation.
//         var db = _mongoClient.GetDatabase(_options.DatabaseName);
//         await db.DropCollectionAsync(_options.EventLogCollectionName);
//         await db.DropCollectionAsync(_options.SequencesCollectionName);
//
//         // Re-create indexes for the next test.
//         await MongoEventStoreAdmin2.EnsureInitializedAsync(_mongoClient, _options);
//     }
//
//     [OneTimeTearDown]
//     public async Task OneTimeTearDown()
//     {
//         //await _mongoClient.DropDatabaseAsync(_options.DatabaseName);
//         _mongoClient.Dispose();
//     }
//
//     /// <summary>
//     /// All writers append to the same stream with ExpectedState.Any.
//     /// All writes must eventually succeed (via retry on duplicate key), and
//     /// the stream must contain every event at a unique, contiguous version.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task ConcurrentAnyWrites_ToSameStream_AllSucceedWithUniqueVersions(CancellationToken ct)
//     {
//         const int writesPerWriter = 20;
//         var streamId = $"shared-{Guid.NewGuid():N}";
//         var totalExpected = WriterCount * writesPerWriter;
//
//         // All writers concurrently append to the same stream.
//         var tasks = _writers.SelectMany((writer, writerIndex) =>
//             Enumerable.Range(0, writesPerWriter).Select(i =>
//                 writer.AppendToStreamAsync(
//                     streamId,
//                     [new AppendEvent<IDomainEvent>(new TestEvent { Value = $"w{writerIndex}-e{i}" })],
//                     new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
//                     ct)));
//
//         // Every task must complete successfully — no exceptions.
//         await Task.WhenAll(tasks);
//
//         // Read back and verify.
//         var readEvents = await _writers[0]
//             .ReadStreamAsync(streamId, cancellationToken: ct)
//             .ToListAsync(ct);
//
//         readEvents.Count.ShouldBe(totalExpected, "All events must be committed");
//
//         var versions = readEvents.Select(e => e.Context.StreamVersion.Value).ToList();
//         versions.ShouldBeUnique("No two events may share a stream version");
//         versions.ShouldBe(Enumerable.Range(0, totalExpected).Select(i => (ulong)i),
//             "Stream versions must be contiguous starting from 0");
//     }
//
//     /// <summary>
//     /// All writers race to create the same stream with StreamDoesNotExist.
//     /// Exactly one must succeed; the rest must receive StreamAppendConflictException.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task ConcurrentStreamCreation_WithStreamDoesNotExist_ExactlyOneSucceeds(CancellationToken ct)
//     {
//         var streamId = $"new-{Guid.NewGuid():N}";
//
//         var results = await Task.WhenAll(_writers.Select(async writer =>
//         {
//             try
//             {
//                 await writer.AppendToStreamAsync(
//                     streamId,
//                     [new AppendEvent<IDomainEvent>(new TestEvent { Value = "create" })],
//                     new AppendToStreamOptions { ExpectedState = ExpectedStreamState.StreamDoesNotExist },
//                     ct);
//                 return (Success: true, Exception: null);
//             }
//             catch (StreamAppendConflictException ex)
//             {
//                 return (Success: false, Exception: ex);
//             }
//         }));
//
//         var successes = results.Count(r => r.Success);
//         var conflicts = results.Count(r => !r.Success);
//
//         successes.ShouldBe(1, "Exactly one writer must succeed in creating the stream");
//         conflicts.ShouldBe(WriterCount - 1, "All other writers must receive a conflict");
//
//         // Verify conflict exceptions are well-formed.
//         foreach (var (_, ex) in results.Where(r => !r.Success))
//         {
//             ex!.StreamId.ShouldBe(streamId);
//             ex.ExpectedState.ShouldBe(ExpectedStreamState.StreamDoesNotExist);
//             ex.ActualState.ShouldBe(StreamState.StreamExists(StreamVersion.FromInt64(0)));
//         }
//
//         // Verify the stream contains exactly one event.
//         var readEvents = await _writers[0]
//             .ReadStreamAsync(streamId, cancellationToken: ct)
//             .ToListAsync(ct);
//
//         readEvents.ShouldHaveSingleItem();
//         readEvents[0].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(0));
//     }
//
//     /// <summary>
//     /// All writers race to append at SpecificVersion(0) after an initial event is written.
//     /// Exactly one must succeed; the rest must receive StreamAppendConflictException.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task ConcurrentSpecificVersionWrites_ExactlyOneSucceeds(CancellationToken ct)
//     {
//         var streamId = $"versioned-{Guid.NewGuid():N}";
//
//         // Seed the stream with one event so all writers can target SpecificVersion(0).
//         await _writers[0].AppendToStreamAsync(
//             streamId,
//             [new AppendEvent<IDomainEvent>(new TestEvent { Value = "seed" })],
//             new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
//             ct);
//
//         var targetVersion = ExpectedStreamState.SpecificVersion(StreamVersion.FromInt64(0));
//
//         var results = await Task.WhenAll(_writers.Select(async writer =>
//         {
//             try
//             {
//                 await writer.AppendToStreamAsync(
//                     streamId,
//                     [new AppendEvent<IDomainEvent>(new TestEvent { Value = "raced" })],
//                     new AppendToStreamOptions { ExpectedState = targetVersion },
//                     ct);
//                 return (Success: true, Exception: (StreamAppendConflictException?)null);
//             }
//             catch (StreamAppendConflictException ex)
//             {
//                 return (Success: false, Exception: ex);
//             }
//         }));
//
//         var successes = results.Count(r => r.Success);
//         var conflicts = results.Count(r => !r.Success);
//
//         successes.ShouldBe(1, "Exactly one writer must win the version race");
//         conflicts.ShouldBe(WriterCount - 1, "All other writers must be rejected");
//
//         // The stream must have exactly 2 events: the seed + the winner.
//         var readEvents = await _writers[0]
//             .ReadStreamAsync(streamId, cancellationToken: ct)
//             .ToListAsync(ct);
//
//         readEvents.Count.ShouldBe(2);
//         readEvents[0].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(0));
//         readEvents[1].Context.StreamVersion.ShouldBe(StreamVersion.FromInt64(1));
//     }
//
//     /// <summary>
//     /// Multiple writers each own independent streams. All writes must succeed and each
//     /// stream must contain exactly the expected events, correctly versioned.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task ConcurrentWrites_ToIndependentStreams_AllSucceed(CancellationToken ct)
//     {
//         const int writesPerWriter = 50;
//
//         var streamIds = _writers.Select(_ => $"indep-{Guid.NewGuid():N}").ToList();
//
//         var tasks = _writers.Select((writer, i) =>
//             Task.WhenAll(Enumerable.Range(0, writesPerWriter).Select(j =>
//                 writer.AppendToStreamAsync(
//                     streamIds[i],
//                     [new AppendEvent<IDomainEvent>(new TestEvent { Value = $"e{j}" })],
//                     new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
//                     ct))));
//
//         await Task.WhenAll(tasks);
//
//         // Each stream must have exactly writesPerWriter events with contiguous versions.
//         foreach (var (streamId, writer) in streamIds.Zip(_writers))
//         {
//             var readEvents = await writer
//                 .ReadStreamAsync(streamId, cancellationToken: ct)
//                 .ToListAsync(ct);
//
//             readEvents.Count.ShouldBe(writesPerWriter, $"Stream {streamId} must have {writesPerWriter} events");
//
//             var versions = readEvents.Select(e => e.Context.StreamVersion.Value).ToList();
//             versions.ShouldBe(Enumerable.Range(0, writesPerWriter).Select(i => (ulong)i));
//         }
//     }
//
//     /// <summary>
//     /// Global positions assigned across all writers must be unique.
//     /// No two events may share a position regardless of which writer committed them.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task ConcurrentWrites_GlobalPositions_AreUnique(CancellationToken ct)
//     {
//         const int writesPerWriter = 30;
//         var streamIds = _writers.Select(_ => $"pos-{Guid.NewGuid():N}").ToList();
//
//         await Task.WhenAll(_writers.Select((writer, i) =>
//             Task.WhenAll(Enumerable.Range(0, writesPerWriter).Select(j =>
//                 writer.AppendToStreamAsync(
//                     streamIds[i],
//                     [new AppendEvent<IDomainEvent>(new TestEvent { Value = $"e{j}" })],
//                     new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
//                     ct)))));
//
//         // Read all events across all streams and verify global position uniqueness.
//         var allPositions = new List<ulong>();
//
//         foreach (var (streamId, writer) in streamIds.Zip(_writers))
//         {
//             var events = await writer
//                 .ReadStreamAsync(streamId, cancellationToken: ct)
//                 .ToListAsync(ct);
//
//             allPositions.AddRange(events.Select(e => e.Context.GlobalPosition.Value.Value));
//         }
//
//         allPositions.Count.ShouldBe(WriterCount * writesPerWriter);
//         allPositions.ShouldBeUnique("Global positions must be unique across all writers");
//     }
//
//     /// <summary>
//     /// Directly proves that two concurrent transactions cannot both increment the sequence counter.
//     /// Transaction A claims the counter and holds the transaction open. While A is open,
//     /// Transaction B attempts to increment — it must receive a WriteConflict (TransientTransactionError).
//     /// Only after A commits can B retry successfully, and the two increments must be serialized.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task SequenceCounter_ConcurrentIncrements_AreSerialised(CancellationToken ct)
//     {
//         var db = _mongoClient.GetDatabase(_options.DatabaseName);
//         var sequence = db.GetCollection<BsonDocument>(_options.SequencesCollectionName);
//
//         var filter = Builders<BsonDocument>.Filter.Eq("_id", "global");
//         var update = Builders<BsonDocument>.Update.Inc("next", 1L);
//         var findOptions = new FindOneAndUpdateOptions<BsonDocument>
//         {
//             ReturnDocument = ReturnDocument.After,
//             IsUpsert = true
//         };
//
//         // Barriers to control interleaving.
//         var txnAHasClaimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
//         var txnBMayProceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
//
//         long txnAValue = -1;
//         long txnBValue = -1;
//         var txnBConflictCount = 0;
//
//         // --- Transaction A: claim, hold open, wait for signal, then commit ---
//         var txnATask = Task.Run(async () =>
//         {
//             using var session = await _mongoClient.StartSessionAsync(cancellationToken: ct);
//             session.StartTransaction();
//
//             var doc = await sequence.FindOneAndUpdateAsync(session, filter, update, findOptions, ct);
//             txnAValue = doc["next"].AsInt64;
//
//             // Signal that A has claimed the counter.
//             txnAHasClaimed.SetResult();
//
//             // Wait until B has attempted (and been blocked), then commit.
//             await txnBMayProceed.Task.WaitAsync(ct);
//             await session.CommitTransactionAsync(ct);
//         }, ct);
//
//         // Wait for A to hold the lock before starting B.
//         await txnAHasClaimed.Task.WaitAsync(ct);
//
//         // --- Transaction B: attempt to increment — must conflict, then retry after A commits ---
//         var txnBTask = Task.Run(async () =>
//         {
//             // Unblock A to commit once B has started its attempt.
//             txnBMayProceed.SetResult();
//
//             while (true)
//             {
//                 using var session = await _mongoClient.StartSessionAsync(cancellationToken: ct);
//                 session.StartTransaction();
//                 try
//                 {
//                     var doc = await sequence.FindOneAndUpdateAsync(session, filter, update, findOptions, ct);
//                     txnBValue = doc["next"].AsInt64;
//                     await session.CommitTransactionAsync(ct);
//                     break;
//                 }
//                 catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError"))
//                 {
//                     txnBConflictCount++;
//                     // Retry.
//                 }
//             }
//         }, ct);
//
//         await Task.WhenAll(txnATask, txnBTask);
//
//         // A committed first, B retried and committed after — values must be strictly ordered.
//         txnAValue.ShouldBe(1L, "Transaction A claims first increment");
//         txnBValue.ShouldBe(2L, "Transaction B claims second increment after A commits");
//         txnBConflictCount.ShouldBeGreaterThanOrEqualTo(1, "Transaction B must have seen at least one WriteConflict");
//     }
//
//     /// <summary>
//     /// Long-running stress test:
//     /// multiple writers append concurrently while a MongoDB change stream observes inserts.
//     /// The observer asserts global position is strictly increasing and contiguous for this test run.
//     /// Fails fast on first ordering violation.
//     /// </summary>
//     [Test]
//     [CancelAfter(TestTimeoutMillis)]
//     public async Task LongRunning_ChangeStream_GlobalPositions_AreStrictlyIncreasingAndContiguous_FailFast(
//         CancellationToken ct)
//     {
//         const int runSeconds = 30;
//         const int minObservedEvents = 200; // Guard against trivial pass.
//         const int writerDelayMs = 0; // Increase to reduce pressure if needed.
//
//         var db = _mongoClient.GetDatabase(_options.DatabaseName);
//         var eventLog = db.GetCollection<BsonDocument>(_options.EventLogCollectionName);
//
//         var runId = Guid.NewGuid().ToString("N");
//
//         using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
//         runCts.CancelAfter(TimeSpan.FromSeconds(runSeconds));
//         var runToken = runCts.Token;
//
//         // Capture first assertion failure from observer and fail after coordinated shutdown.
//         Exception? firstFailure = null;
//
//         // Watch inserts on event log. We filter to this test run inside the loop.
//         var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>()
//             .Match(x => x.OperationType == ChangeStreamOperationType.Insert);
//
//         var changeStreamOptions = new ChangeStreamOptions
//         {
//             FullDocument = ChangeStreamFullDocumentOption.Default
//         };
//
//         // Open change stream before writers start so we do not miss early inserts.
//         using var cursor = await eventLog.WatchAsync(pipeline, changeStreamOptions, runToken);
//
//         var observerTask = Task.Run(async () =>
//         {
//             long? lastPos = null;
//             var observed = 0;
//
//             try
//             {
//                 await foreach (var change in cursor.ToAsyncEnumerable().WithCancellation(runToken))
//                 {
//                     var doc = change.FullDocument;
//                     if (doc is null)
//                         continue;
//
//                     var pos = doc["_id"].AsInt64;
//
//                     if (lastPos.HasValue && pos != lastPos.Value + 1)
//                     {
//                         firstFailure ??= new ShouldAssertException(
//                             $"Global position not contiguous. Last={lastPos.Value}, Current={pos}, RunId={runId}");
//                         await runCts.CancelAsync(); // Fail fast: stop all writers and observer quickly.
//                         return;
//                     }
//
//                     lastPos = pos;
//                     observed++;
//                 }
//             }
//             catch (OperationCanceledException) when (runToken.IsCancellationRequested)
//             {
//                 // Expected when run duration elapses or fail-fast cancellation occurs.
//             }
//
//             if (firstFailure is null)
//             {
//                 observed.ShouldBeGreaterThanOrEqualTo(
//                     minObservedEvents,
//                     $"Expected to observe at least {minObservedEvents} events for RunId={runId}");
//             }
//         }, runToken);
//
//         var writerTasks = _writers
//             .Select((writer, writerIndex) => Task.Run(async () =>
//             {
//                 var streamId = $"lr-{runId}-w{writerIndex}";
//                 var sequence = 0;
//
//                 while (!runToken.IsCancellationRequested)
//                 {
//                     await writer.AppendToStreamAsync(
//                         streamId,
//                         [
//                             new AppendEvent<IDomainEvent>(new TestEvent
//                                 { Value = $"{runId}|w{writerIndex}|e{sequence++}" })
//                         ],
//                         new AppendToStreamOptions { ExpectedState = ExpectedStreamState.Any },
//                         runToken);
//
//                     if (writerDelayMs > 0)
//                         await Task.Delay(writerDelayMs, runToken);
//                 }
//             }, runToken))
//             .ToList();
//
//         // Wait for run end or fail-fast cancellation.
//         try
//         {
//             await Task.WhenAll(writerTasks.Append(observerTask));
//         }
//         catch (OperationCanceledException) when (runToken.IsCancellationRequested)
//         {
//             // Normal shutdown path.
//         }
//
//         // Surface the first observer failure as the test failure.
//         if (firstFailure is not null)
//             throw firstFailure;
//     }
//
//     // -------------------------------------------------------------------------
//     // Inner types
//     // -------------------------------------------------------------------------
//
//     private interface IDomainEvent;
//
//     private record TestEvent : IDomainEvent
//     {
//         public required string Value { get; init; }
//     }
// }