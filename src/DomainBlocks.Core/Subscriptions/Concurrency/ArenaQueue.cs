using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DomainBlocks.Core.Subscriptions.Concurrency;

internal class ArenaQueue<T> where T : class, new()
{
    private readonly Channel<int> _emptyChannel;
    private readonly Channel<int> _fullChannel;
    private readonly T[] _arena;

    public ArenaQueue(int size)
    {
        _emptyChannel = Channel.CreateBounded<int>(size);
        _fullChannel = Channel.CreateBounded<int>(size);
        _arena = new T[size];

        for (var i = 0; i < _arena.Length; i++)
        {
            _arena[i] = new T();
            Debug.Assert(_emptyChannel.Writer.TryWrite(i));
        }
    }

    public async Task WriteAsync(Action<T> onWriting, CancellationToken cancellationToken = default)
    {
        var index = await _emptyChannel.Reader.ReadAsync(cancellationToken);
        onWriting(_arena[index]);
        await _fullChannel.Writer.WriteAsync(index, cancellationToken);
    }

    public async IAsyncEnumerable<T> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _fullChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_fullChannel.Reader.TryRead(out var index))
            {
                yield return _arena[index];
                await _emptyChannel.Writer.WriteAsync(index, cancellationToken);
            }
        }
    }
}