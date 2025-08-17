using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DomainBlocks.EventSourcing;

internal static class TypeExtensions
{
    public static string GetPrettyName(this Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var sb = new StringBuilder();
        sb.Append(type.Name[..type.Name.IndexOf('`')]);
        sb.Append('<');
        sb.Append(string.Join(", ", type.GetGenericArguments().Select(GetPrettyName)));
        sb.Append('>');

        return sb.ToString();
    }

    public static bool HasInterface(this Type type, Type interfaceType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(interfaceType);

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
        ArgumentNullException.ThrowIfNull(type);

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
        this Type type,
        Type other,
        [NotNullWhen(true)] out IReadOnlyDictionary<Type, Type>? results)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(other);

        var internalResults = new Dictionary<Type, Type>();
        var success = TryResolveImpl(type, other);
        results = success ? internalResults : null;
        return success;

        // E.g. EntityBase<TState> is "resolvable" from MyEntity : EntityBase<MyState>
        bool TryResolveImpl(Type lhsType, Type rhsType)
        {
            if (lhsType.IsGenericParameter)
            {
                // Return early if LHS has already been added to the results. This avoids infinite recursion when the
                // Curiously Recurring Template Pattern (CRTP) is used, e.g.:
                // class EntityBase<TState> where TState : StateBase<TState>
                if (!internalResults.TryAdd(lhsType, rhsType))
                {
                    return true;
                }

                // Check LHS is compatible with any RHS type constraints.
                return lhsType.GetGenericParameterConstraints().All(c => TryResolveImpl(c, rhsType));
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
}