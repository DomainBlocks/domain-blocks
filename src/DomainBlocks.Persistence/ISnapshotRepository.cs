﻿using System.Threading;
using System.Threading.Tasks;

namespace DomainBlocks.Persistence;

public interface ISnapshotRepository
{
    Task SaveSnapshotAsync<TState>(
        string snapshotKey,
        long snapshotVersion,
        TState snapshotState,
        CancellationToken cancellationToken = default);

    Task<(bool isSuccess, Snapshot<TState> snapshot)> TryLoadSnapshotAsync<TState>(
        string snapshotKey,
        CancellationToken cancellationToken = default);
}
