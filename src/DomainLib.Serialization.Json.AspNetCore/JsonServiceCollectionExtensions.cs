using System;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Serialization.Json.AspNetCore
{
    public static class JsonServiceCollectionExtensions
    {
        public static IServiceCollection AddJsonSerializer(this IServiceCollection services)
        {
            return services;
        }
    }
}
