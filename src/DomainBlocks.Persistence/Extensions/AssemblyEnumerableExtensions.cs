using System.Reflection;

namespace DomainBlocks.Persistence.Extensions;

internal static class AssemblyEnumerableExtensions
{
    public static IEnumerable<Type> FindConcreteTypesAssignableTo<T>(this IEnumerable<Assembly> assemblies)
    {
        return assemblies.FindConcreteTypesAssignableTo(typeof(T));
    }

    public static IEnumerable<Type> FindConcreteTypesAssignableTo(this IEnumerable<Assembly> assemblies, Type type)
    {
        return from assembly in assemblies
            from t in assembly.GetTypes()
            where t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(type)
            select t;
    }
}