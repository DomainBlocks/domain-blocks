using System.Reflection;

namespace DomainBlocks.Experimental.EventSourcing.Extensions;

internal static class TypeExtensions
{
    public static IEnumerable<(Type ArgType, MethodInfo Method)> FindEventApplierMethods(
        this Type type, string methodName, bool isNonPublicAllowed)
    {
        return FindEventApplierMethods(type, methodName, isNonPublicAllowed, false);
    }

    public static IEnumerable<(Type ArgType, MethodInfo Method)> FindImmutableEventApplierMethods(
        this Type type, string methodName, bool isNonPublicAllowed)
    {
        return FindEventApplierMethods(type, methodName, isNonPublicAllowed, true);
    }

    public static IEnumerable<Type> FindAssignableConcreteTypes(this Type type)
    {
        return from assembly in AppDomain.CurrentDomain.GetAssemblies()
            from t in assembly.GetTypes()
            where t.IsAssignableTo(type) &&
                  t is { IsAbstract: false, IsInterface: false }
            select t;
    }

    private static IEnumerable<(Type ArgType, MethodInfo Method)> FindEventApplierMethods(
        Type type, string methodName, bool isNonPublicAllowed, bool isImmutable)
    {
        var bindingFlags = BindingFlags.Public | BindingFlags.Instance;

        if (isNonPublicAllowed)
        {
            bindingFlags |= BindingFlags.NonPublic;
        }

        var methods = from method in type.GetMethods(bindingFlags)
            where !method.IsStatic
            where method.Name == methodName
            let @params = method.GetParameters()
            where @params.Length == 1
            let arg = @params[0]
            let argType = arg.ParameterType
            let returnType = method.ReturnParameter.ParameterType
            where !argType.IsAssignableTo(typeof(Delegate))
            where !arg.IsOut
            where !argType.IsByRef
            where isImmutable ? returnType.IsAssignableTo(type) : returnType == typeof(void)
            select (argType, method);

        return methods.ToList();
    }
}