using System.Collections.Generic;

namespace DomainBlocks.Projections.New;

public interface IProjectionOptionsProvider
{
    public IEnumerable<IProjectionOptions> GetProjectionOptions();
}