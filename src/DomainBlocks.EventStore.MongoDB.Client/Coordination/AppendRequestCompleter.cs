using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class AppendRequestCompleter : IAsyncDisposable, IAppendRequestCompleter
{
    private readonly IMongoCollection<AppendRequest> _appendRequests;
    private readonly ILogger<AppendRequestCompleter> _logger;
    private readonly Channel<IReadOnlyCollection<Guid>> _channel;
    private readonly Task _processTask;

    public AppendRequestCompleter(
        IMongoCollection<AppendRequest> appendRequests,
        ILogger<AppendRequestCompleter> logger)
    {
        _appendRequests = appendRequests;
        _logger = logger;
        _channel = Channel.CreateUnbounded<IReadOnlyCollection<Guid>>(
            new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });
        _processTask = ProcessAsync();
    }

    public void Complete(IReadOnlyCollection<Guid> commitIds)
    {
        if (commitIds.Count > 0)
            _channel.Writer.TryWrite(commitIds);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _processTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task ProcessAsync()
    {
        _logger.LogDebug("Request completer started");

        try
        {
            while (await _channel.Reader.WaitToReadAsync())
            {
                var commitIds = new HashSet<Guid>();

                while (_channel.Reader.TryRead(out var batch))
                {
                    foreach (var id in batch)
                        commitIds.Add(id);
                }

                if (commitIds.Count == 0)
                    continue;

                var filter = Builders<AppendRequest>.Filter.In(x => x.CommitId, commitIds) &
                             Builders<AppendRequest>.Filter.Eq(x => x.CompletedAtUtc, null);

                var update = Builders<AppendRequest>.Update.CurrentDate(x => x.CompletedAtUtc);

                var result = await _appendRequests.UpdateManyAsync(filter, update);

                _logger.LogDebug("Marked {Count} request(s) as completed", result.MatchedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request completer failed");
        }

        _logger.LogDebug("Request completer stopped");
    }
}