using System.Collections.Generic;

namespace DomainBlocks.Projections.Sql
{
    public sealed class SqlColumnDefinitions : Dictionary<string, SqlColumnDefinition>
    {
        public SqlColumnDefinitions()
        {
        }

        public SqlColumnDefinitions(IEnumerable<KeyValuePair<string, SqlColumnDefinition>> collection) : base(collection)
        {
        }
    }
}