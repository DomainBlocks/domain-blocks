using System.Reflection;
using System.Text;

namespace DomainBlocks.Experimental.Persistence.Extensions;

public static class TypeExtensions
{
    public static string GetPrettyName(this Type type)
    {
        if (!type.IsGenericType) return type.Name;

        var sb = new StringBuilder();
        sb.Append(type.Name[..type.Name.IndexOf('`')]);
        sb.Append('<');
        sb.Append(string.Join(", ", type.GetGenericArguments().Select(GetPrettyName)));
        sb.Append('>');

        return sb.ToString();
    }

    public static bool HasInterface(this Type type, Type interfaceType)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));

        if (!interfaceType.IsInterface)
            throw new ArgumentException("Type must be an interface.", nameof(interfaceType));

        if (interfaceType.IsGenericTypeDefinition)
        {
            return type
                .GetInterfaces()
                .Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == interfaceType);
        }

        return type.GetInterfaces().Any(x => x == interfaceType);
    }

    public static IReadOnlySet<Type> FindReachableGenericParameters(this Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        var results = new HashSet<Type>();
        FindImpl(type);
        return results;

        void FindImpl(Type currentType)
        {
            if (currentType.IsGenericParameter)
            {
                if (!results.Add(currentType)) return;

                foreach (var constraint in currentType.GetGenericParameterConstraints())
                {
                    FindImpl(constraint);
                }
            }
            else if (currentType.ContainsGenericParameters)
            {
                foreach (var arg in currentType.GetGenericArguments())
                {
                    FindImpl(arg);
                }
            }
        }
    }

    public static bool TryResolveGenericParametersFrom(
        this Type type, Type other, out IReadOnlyDictionary<Type, Type>? results)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (other == null) throw new ArgumentNullException(nameof(other));

        var internalResults = new Dictionary<Type, Type>();
        var success = TryResolveImpl(type, other);
        results = success ? internalResults : null;
        return success;

        // E.g. EntityBase<TState> is "resolvable" from MyEntity : EntityBase<FooState>
        bool TryResolveImpl(Type lhsType, Type rhsType)
        {
            if (lhsType.IsGenericParameter)
            {
                // Assume RHS is a match for this generic parameter, and disprove below. This also allows us to return
                // early if it has already been added to the results, avoiding infinite recursion when the Curiously
                // Recurring Template Pattern (CRTP) is used, e.g.:
                // class EntityBase<TState> where TState : StateBase<TState>
                if (!internalResults.TryAdd(lhsType, rhsType))
                {
                    return true;
                }

                // Check the LHS is compatible with any RHS constraints.
                return rhsType.CanSatisfyConstraints(lhsType.GenericParameterAttributes) &&
                       lhsType.GetGenericParameterConstraints().All(c => TryResolveImpl(c, rhsType));
            }

            if (lhsType.ContainsGenericParameters)
            {
                // LHS still has generic parameters. Recursively resolve.
                var lhsGenericTypeDef = lhsType.GetGenericTypeDefinition();
                var matchingRhsType = rhsType;

                while (matchingRhsType != null)
                {
                    if (matchingRhsType.IsGenericType &&
                        matchingRhsType.GetGenericTypeDefinition() == lhsGenericTypeDef)
                    {
                        break;
                    }

                    matchingRhsType = matchingRhsType.BaseType;
                }

                // If no matching class found in the inheritance hierarchy, check interfaces.
                matchingRhsType ??= rhsType
                    .GetInterfaces()
                    .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == lhsGenericTypeDef);

                if (matchingRhsType == null)
                {
                    // RHS has no matching generic type definition.
                    return false;
                }

                var lhsArgs = lhsType.GetGenericArguments();
                var rhsArgs = matchingRhsType.GetGenericArguments();

                for (var i = 0; i < lhsArgs.Length; i++)
                {
                    if (!TryResolveImpl(lhsArgs[i], rhsArgs[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            // The type to match has no generic parameters. Check directly for assignability.
            return lhsType.IsAssignableFrom(rhsType);
        }
    }

    // TODO: finish
    private static bool CanSatisfyConstraints(this Type type, GenericParameterAttributes genericParameterAttributes)
    {
        if (genericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))
        {
            if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
            {
                return false;
            }
        }

        return true;
    }
}