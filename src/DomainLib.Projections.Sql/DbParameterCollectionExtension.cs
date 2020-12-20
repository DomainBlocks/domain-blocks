using System;
using System.Data;
using System.Linq;

namespace DomainLib.Projections.Sql
{
    public static class DataParameterCollectionExtension
    {
        public static string ToFormattedString(this IDataParameterCollection collection)
        {
            return string.Join($", {Environment.NewLine}",
                               collection.Cast<IDataParameter>()
                                         .Select(p => $"Name: {p.ParameterName}, " +
                                                      $"Type: {p.DbType}, Value: {p.Value}"));
        }
    }
}