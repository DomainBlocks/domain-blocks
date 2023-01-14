using DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions;
using Microsoft.Extensions.Logging;
using Pattern = DomainBlocks.ThirdParty.SqlStreamStore.Streams.Pattern;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    /// <summary>
    ///     Represents a base implementation of a readonly stream store.
    /// </summary>
    public abstract class ReadonlyStreamStoreBase : IReadonlyStreamStore
    {
        private const int DefaultReloadInterval = 3000;
        protected readonly GetUtcNow GetUtcNow;
        protected readonly ILogger Logger;
        private bool _isDisposed;
        private readonly MetadataMaxAgeCache _metadataMaxAgeCache;
        private readonly bool _disableMetadataCache;

        protected ReadonlyStreamStoreBase(
            TimeSpan metadataMaxAgeCacheExpiry,
            int metadataMaxAgeCacheMaxSize,
            GetUtcNow getUtcNow,
            string logName)
        {
            GetUtcNow = getUtcNow ?? SystemClock.GetUtcNow;
            Logger = Logging.Log.Create(logName);

            _metadataMaxAgeCache = new MetadataMaxAgeCache(this, metadataMaxAgeCacheExpiry,
                metadataMaxAgeCacheMaxSize, GetUtcNow);
        }

        protected ReadonlyStreamStoreBase(GetUtcNow getUtcNow, string logName)
        {
            GetUtcNow = getUtcNow ?? SystemClock.GetUtcNow;
            Logger = Logging.Log.Create(logName);
            _disableMetadataCache = true;
        }

        public async Task<Streams.ReadAllPage> ReadAllForwards(
            long fromPositionInclusive,
            int maxCount,
            bool prefetchJsonData,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(fromPositionInclusive, nameof(fromPositionInclusive)).IsGte(0);
            Ensure.That(maxCount, nameof(maxCount)).IsGte(1);

            GuardAgainstDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogDebug("ReadAllForwards from position {fromPositionInclusive} with max count " +
                            "{maxCount}.", fromPositionInclusive, maxCount);

            Task<Streams.ReadAllPage> ReadNext(long nextPosition, CancellationToken ct) => ReadAllForwards(nextPosition, maxCount, prefetchJsonData, ct);

            var page = await ReadAllForwardsInternal(fromPositionInclusive, maxCount, prefetchJsonData, ReadNext, cancellationToken)
                .ConfigureAwait(false);

            // https://github.com/damianh/SqlStreamStore/issues/31
            // Under heavy parallel load, gaps may appear in the position sequence due to sequence
            // number reservation of in-flight transactions.
            // Here we check if there are any gaps, and in the unlikely event there is, we delay a little bit
            // and re-issue the read. This is expected
            if(!page.IsEnd || page.Messages.Length <= 1)
            {
                return await FilterExpired(page, ReadNext, cancellationToken).ConfigureAwait(false);
            }

            // Check for gap between last page and this.
            if (page.Messages[0].Position != fromPositionInclusive)
            {
                page = await ReloadAfterDelay(fromPositionInclusive, maxCount, prefetchJsonData, ReadNext, cancellationToken);
            }

            // check for gap in messages collection
            for(int i = 0; i < page.Messages.Length - 1; i++)
            {
                if(page.Messages[i].Position + 1 != page.Messages[i + 1].Position)
                {
                    page = await ReloadAfterDelay(fromPositionInclusive, maxCount, prefetchJsonData, ReadNext, cancellationToken);
                    break;
                }
            }

            return await FilterExpired(page, ReadNext, cancellationToken).ConfigureAwait(false);
        }

        public async Task<Streams.ReadAllPage> ReadAllBackwards(
            long fromPositionInclusive,
            int maxCount,
            bool prefetchJsonData,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(fromPositionInclusive, nameof(fromPositionInclusive)).IsGte(-1);
            Ensure.That(maxCount, nameof(maxCount)).IsGte(1);

            GuardAgainstDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogDebug(
                "ReadAllBackwards from position {fromPositionInclusive} with max count {maxCount}.",
                fromPositionInclusive,
                maxCount);

            Streams.ReadNextAllPage readNext = (nextPosition, ct) => ReadAllBackwards(nextPosition, maxCount, prefetchJsonData, ct);
            var page = await ReadAllBackwardsInternal(fromPositionInclusive, maxCount, prefetchJsonData, readNext, cancellationToken);
            return await FilterExpired(page, readNext, cancellationToken);
        }

        public async Task<Streams.ReadStreamPage> ReadStreamForwards(
            Streams.StreamId streamId,
            int fromVersionInclusive,
            int maxCount,
            bool prefetchJsonData = true,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(fromVersionInclusive, nameof(fromVersionInclusive)).IsGte(0);
            Ensure.That(maxCount, nameof(maxCount)).IsGte(1);

            GuardAgainstDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogDebug(
                "ReadStreamForwards {streamId} from version {fromVersionInclusive} with max count {maxCount}.",
                streamId,
                fromVersionInclusive,
                maxCount);

            Streams.ReadNextStreamPage readNext = (nextVersion, ct) => ReadStreamForwards(streamId, nextVersion, maxCount, prefetchJsonData, ct);
            var page = await ReadStreamForwardsInternal(streamId, fromVersionInclusive, maxCount, prefetchJsonData,
                readNext, cancellationToken);
            return await FilterExpired(page, readNext, cancellationToken);
        }

        public async Task<Streams.ReadStreamPage> ReadStreamBackwards(
            Streams.StreamId streamId,
            int fromVersionInclusive,
            int maxCount,
            bool prefetchJsonData = true,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(fromVersionInclusive, nameof(fromVersionInclusive)).IsGte(-1);
            Ensure.That(maxCount, nameof(maxCount)).IsGte(1);

            GuardAgainstDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogDebug(
                "ReadStreamBackwards {streamId} from version {fromVersionInclusive} with max count {maxCount}.",
                streamId,
                fromVersionInclusive,
                maxCount);

            Streams.ReadNextStreamPage readNext =
                (nextVersion, ct) => ReadStreamBackwards(streamId, nextVersion, maxCount, prefetchJsonData, ct);
            var page = await ReadStreamBackwardsInternal(streamId, fromVersionInclusive, maxCount, prefetchJsonData, readNext,
                cancellationToken);
            return await FilterExpired(page, readNext, cancellationToken);
        }

        public IStreamSubscription SubscribeToStream(
            Streams.StreamId streamId,
            int? continueAfterVersion,
            StreamMessageReceived streamMessageReceived,
            SubscriptionDropped subscriptionDropped = null,
            HasCaughtUp hasCaughtUp = null,
            bool prefetchJsonData = true,
            string name = null)
        {
            Ensure.That(streamId, nameof(streamId)).IsNotNull();
            Ensure.That(streamMessageReceived, nameof(streamMessageReceived)).IsNotNull();

            GuardAgainstDisposed();

            return SubscribeToStreamInternal(
                streamId,
                continueAfterVersion,
                streamMessageReceived,
                subscriptionDropped,
                hasCaughtUp,
                prefetchJsonData,
                name);
        }

        public IAllStreamSubscription SubscribeToAll(
            long? continueAfterPosition,
            AllStreamMessageReceived streamMessageReceived,
            AllSubscriptionDropped subscriptionDropped = null,
            HasCaughtUp hasCaughtUp = null,
            bool prefetchJsonData = true,
            string name = null)
        {
            Ensure.That(streamMessageReceived, nameof(streamMessageReceived)).IsNotNull();

            GuardAgainstDisposed();

            return SubscribeToAllInternal(
                continueAfterPosition,
                streamMessageReceived,
                subscriptionDropped,
                hasCaughtUp,
                prefetchJsonData,
                name);
        }

        public Task<Streams.StreamMetadataResult> GetStreamMetadata(
           string streamId,
           CancellationToken cancellationToken = default)
        {
            if (streamId == null) throw new ArgumentNullException(nameof(streamId));
            if (streamId.StartsWith("$") && streamId != Deleted.DeletedStreamId)
            {
                throw new ArgumentException("Must not start with '$'", nameof(streamId));
            }

            Logger.LogDebug("GetStreamMetadata {streamId}.", streamId);

            return GetStreamMetadataInternal(streamId, cancellationToken);
        }

        public Task<long> ReadHeadPosition(CancellationToken cancellationToken = default)
        {
            GuardAgainstDisposed();

            return ReadHeadPositionInternal(cancellationToken);
        }

        public Task<long> ReadStreamHeadPosition(Streams.StreamId streamId, CancellationToken cancellationToken = default)
        {
            if (streamId == null) throw new ArgumentNullException(nameof(streamId));

            GuardAgainstDisposed();

            return ReadStreamHeadPositionInternal(streamId, cancellationToken);
        }

        public Task<int> ReadStreamHeadVersion(Streams.StreamId streamId, CancellationToken cancellationToken = default)
        {
            if (streamId == null) throw new ArgumentNullException(nameof(streamId));

            GuardAgainstDisposed();

            return ReadStreamHeadVersionInternal(streamId, cancellationToken);
        }

        public Task<Streams.ListStreamsPage> ListStreams(
            int maxCount = 100,
            string continuationToken = default,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(maxCount).IsGt(0);

            GuardAgainstDisposed();

            return ListStreams(Pattern.Anything(), maxCount, continuationToken, cancellationToken);
        }

        public Task<Streams.ListStreamsPage> ListStreams(
            Streams.Pattern pattern,
            int maxCount = 100,
            string continuationToken = default,
            CancellationToken cancellationToken = default)
        {
            Ensure.That(maxCount).IsGt(0);
            Ensure.That(pattern).IsNotNull();

            Task<Streams.ListStreamsPage> ListNext(string @continue, CancellationToken ct)
                => ListStreams(pattern, maxCount, @continue, ct);

            return ListStreamsInternal(pattern, maxCount, continuationToken, ListNext, cancellationToken);
        }

        public void Dispose()
        {
            OnDispose?.Invoke();
            Dispose(true);
            GC.SuppressFinalize(this);
            _isDisposed = true;
        }

        public event Action OnDispose;

        protected abstract Task<Streams.ReadAllPage> ReadAllForwardsInternal(
            long fromPositionExclusive,
            int maxCount,
            bool prefetch,
            Streams.ReadNextAllPage readNext,
            CancellationToken cancellationToken);

        protected abstract Task<Streams.ReadAllPage> ReadAllBackwardsInternal(
            long fromPositionExclusive,
            int maxCount,
            bool prefetch,
            Streams.ReadNextAllPage readNext,
            CancellationToken cancellationToken);

        protected abstract Task<Streams.ReadStreamPage> ReadStreamForwardsInternal(
            string streamId,
            int start,
            int count,
            bool prefetch,
            Streams.ReadNextStreamPage readNext,
            CancellationToken cancellationToken);

        protected abstract Task<Streams.ReadStreamPage> ReadStreamBackwardsInternal(
            string streamId,
            int fromVersionInclusive,
            int count,
            bool prefetch,
            Streams.ReadNextStreamPage readNext, CancellationToken cancellationToken);

        protected abstract Task<long> ReadHeadPositionInternal(CancellationToken cancellationToken);
        protected abstract Task<long> ReadStreamHeadPositionInternal(string streamId, CancellationToken cancellationToken);
        protected abstract Task<int> ReadStreamHeadVersionInternal(string streamId, CancellationToken cancellationToken);

        protected abstract IStreamSubscription SubscribeToStreamInternal(
            string streamId,
            int? startVersion,
            StreamMessageReceived streamMessageReceived,
            SubscriptionDropped subscriptionDropped,
            HasCaughtUp hasCaughtUp,
            bool prefetchJsonData,
            string name);

        protected abstract IAllStreamSubscription SubscribeToAllInternal(
            long? fromPosition,
            AllStreamMessageReceived streamMessageReceived,
            AllSubscriptionDropped subscriptionDropped,
            HasCaughtUp hasCaughtUp,
            bool prefetchJsonData,
            string name);

        protected abstract Task<Streams.StreamMetadataResult> GetStreamMetadataInternal(
            string streamId,
            CancellationToken cancellationToken);

        protected abstract Task<Streams.ListStreamsPage> ListStreamsInternal(
            Streams.Pattern pattern,
            int maxCount,
            string continuationToken,
            Streams.ListNextStreamsPage listNextStreamsPage,
            CancellationToken cancellationToken);

        protected virtual void Dispose(bool disposing)
        {}

        protected void GuardAgainstDisposed()
        {
            if(_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        protected abstract void PurgeExpiredMessage(Streams.StreamMessage streamMessage);

        private async Task<Streams.ReadAllPage> ReloadAfterDelay(
            long fromPositionInclusive,
            int maxCount,
            bool prefetch,
            Streams.ReadNextAllPage readNext,
            CancellationToken cancellationToken)
        {
            Logger.LogInformation("ReadAllForwards: gap detected in position, reloading after {DefaultReloadInterval}ms", DefaultReloadInterval);
            await Task.Delay(DefaultReloadInterval, cancellationToken);
            var reloadedPage = await ReadAllForwardsInternal(fromPositionInclusive, maxCount, prefetch, readNext, cancellationToken)
                .ConfigureAwait(false);
            return await FilterExpired(reloadedPage, readNext, cancellationToken).ConfigureAwait(false);
        }

        private async Task<Streams.ReadStreamPage> FilterExpired(
            Streams.ReadStreamPage page,
            Streams.ReadNextStreamPage readNext,
            CancellationToken cancellationToken)
        {
            if(page.StreamId.StartsWith("$"))
            {
                return page;
            }

            int? maxAge = _metadataMaxAgeCache == null
                ? null
                : await _metadataMaxAgeCache.GetMaxAge(page.StreamId, cancellationToken);
            if (!maxAge.HasValue)
            {
                return page;
            }
            var currentUtc = GetUtcNow();
            var valid = new List<Streams.StreamMessage>();
            foreach(var message in page.Messages)
            {
                if(message.CreatedUtc.AddSeconds(maxAge.Value) > currentUtc)
                {
                    valid.Add(message);
                }
                else
                {
                    PurgeExpiredMessage(message);
                }
            }
            return new Streams.ReadStreamPage(
                page.StreamId,
                page.Status,
                page.FromStreamVersion,
                page.NextStreamVersion,
                page.LastStreamVersion,
                page.LastStreamPosition,
                page.ReadDirection,
                page.IsEnd,
                readNext,
                valid.ToArray());
        }

        private async Task<Streams.ReadAllPage> FilterExpired(
           Streams.ReadAllPage readAllPage,
           Streams.ReadNextAllPage readNext,
           CancellationToken cancellationToken)
        {
            if(_disableMetadataCache)
            {
                return readAllPage;
            }
            var valid = new List<Streams.StreamMessage>();
            var currentUtc = GetUtcNow();
            foreach (var streamMessage in readAllPage.Messages)
            {
                if(streamMessage.StreamId.StartsWith("$"))
                {
                    valid.Add(streamMessage);
                    continue;
                }
                int? maxAge = _metadataMaxAgeCache == null
                    ? null
                    : await _metadataMaxAgeCache.GetMaxAge(streamMessage.StreamId, cancellationToken);
                if (!maxAge.HasValue)
                {
                    valid.Add(streamMessage);
                    continue;
                }
                if (streamMessage.CreatedUtc.AddSeconds(maxAge.Value) > currentUtc)
                {
                    valid.Add(streamMessage);
                }
                else
                {
                    PurgeExpiredMessage(streamMessage);
                }
            }
            return new Streams.ReadAllPage(
                readAllPage.FromPosition,
                readAllPage.NextPosition,
                readAllPage.IsEnd,
                readAllPage.Direction,
                readNext,
                valid.ToArray());
        }

        protected List<Streams.StreamMessage> FilterExpired(List<(Streams.StreamMessage StreamMessage, int? MaxAge)> messages)
        {
            var valid = new List<StreamMessage>();
            var currentUtc = GetUtcNow();
            foreach (var item in messages)
            {
                if (item.StreamMessage.StreamId.StartsWith("$"))
                {
                    valid.Add(item.StreamMessage);
                    continue;
                }
                if (!item.MaxAge.HasValue || item.MaxAge <= 0)
                {
                    valid.Add(item.StreamMessage);
                    continue;
                }
                if (item.StreamMessage.CreatedUtc.AddSeconds(item.MaxAge.Value) > currentUtc)
                {
                    valid.Add(item.StreamMessage);
                }
                else
                {
                    PurgeExpiredMessage(item.StreamMessage);
                }
            }
            return valid;
        }

        ~ReadonlyStreamStoreBase()
        {
            Dispose(false);
        }
    }
}
