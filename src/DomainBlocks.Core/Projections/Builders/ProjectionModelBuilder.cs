namespace DomainBlocks.Core.Projections.Builders;

public class ProjectionModelBuilder
{
    private readonly List<IProjectionOptionsBuilder> _projectionOptionsBuilders = new();

    public ProjectionModelBuilder Projection<TState>(Action<ProjectionOptionsBuilder<TState>> builderAction)
    {
        if (builderAction == null) throw new ArgumentNullException(nameof(builderAction));
        var builder = new ProjectionOptionsBuilder<TState>();
        _projectionOptionsBuilders.Add(builder);
        builderAction(builder);
        return this;
    }

    public ProjectionRegistry Build()
    {
        return _projectionOptionsBuilders
            .Select(x => x.Options)
            .Aggregate(new ProjectionRegistry(), (acc, next) => next.Register(acc));
    }
}