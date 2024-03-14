using DomainBlocks.Experimental.Persistence.Adapters;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class EntityAdapterExtensions
{
    public static EntityAdapter<TEntity> HideStateType<TEntity, TState>(
        this IEntityAdapter<TEntity, TState> entityAdapter)
    {
        return new EntityAdapter<TEntity>(
            typeof(TState),
            entityAdapter.GetId,
            x => entityAdapter.GetCurrentState(x)!,
            entityAdapter.GetRaisedEvents,
            EntityRestorer,
            () => entityAdapter.CreateState()!);

        async Task<TEntity> EntityRestorer(object initialState, IAsyncEnumerable<object> events, CancellationToken ct)
        {
            var currentState = (TState)initialState;

            await foreach (var e in events.WithCancellation(ct))
            {
                currentState = entityAdapter.Fold(currentState, e);
            }

            return entityAdapter.Create(currentState);
        }
    }
}