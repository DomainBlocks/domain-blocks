namespace DomainBlocks.Projections.New;

public interface IProjectionOptions
{
    ProjectionRegistry Register(ProjectionRegistry registry);
}