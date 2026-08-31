using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DomainBlocks.EventSourcing;

public static class TypeExtensions
{
    extension(Type type)
    {
        public string GetPrettyName()
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

        public bool HasInterface(Type interfaceType)
        {
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

        /// <summary>
        /// Attempts to bind the generic parameters of an open generic type to the corresponding type arguments of an
        /// assignable closed type.
        /// </summary>
        /// <param name="closedType">The closed type to infer bindings from.</param>
        /// <param name="bindings">
        /// When this method returns <c>true</c>, contains a mapping from each generic parameter to its inferred type
        /// binding.
        /// </param>
        /// <returns>
        /// <c>true</c> if the generic parameters of <paramref name="type"/> can be bound based on
        /// <paramref name="closedType"/> such that, when substituted with those bindings, the resulting type is
        /// assignable from <paramref name="closedType"/>.
        /// </returns>
        public bool TryBindGenericParameters(
            Type closedType,
            [NotNullWhen(true)] out IReadOnlyDictionary<Type, Type>? bindings)
        {
            if (closedType.ContainsGenericParameters)
            {
                bindings = null;
                return false;
            }

            var internalBindings = new Dictionary<Type, Type>();
            var success = Bind(type, closedType);

            bindings = success ? internalBindings.ToFrozenDictionary() : null;
            return success;

            // E.g. LHS = Aggregate<TState>, RHS = MyAggregate : Aggregate<MyState>, binding = TState -> MyState.
            bool Bind(Type lhsType, Type rhsType)
            {
                // Case 1: LHS is a closed type - check assignability.
                if (!lhsType.ContainsGenericParameters)
                    return lhsType.IsAssignableFrom(rhsType);

                // Case 2: LHS is a generic parameter - check constraints.
                if (lhsType.IsGenericParameter)
                {
                    // Prevent infinite recursion when generic parameters are self-referential, e.g. CRTP.
                    if (!internalBindings.TryAdd(lhsType, rhsType))
                        return true;

                    return lhsType.GetGenericParameterConstraints().All(c => Bind(c, rhsType));
                }

                // Case 3: LHS has generic parameters. Recursively bind.
                var rhsMatchingType = GetMatchingGenericType(lhsType, rhsType);
                if (rhsMatchingType == null)
                    return false;

                var lhsArgs = lhsType.GetGenericArguments();
                var rhsArgs = rhsMatchingType.GetGenericArguments();

                return !lhsArgs.Where((t, i) => !Bind(t, rhsArgs[i])).Any();
            }

            static Type? GetMatchingGenericType(Type lhsType, Type rhsType)
            {
                var lhsGenericDef = lhsType.GetGenericTypeDefinition();

                // Walk base types
                for (var t = rhsType; t != null; t = t.BaseType)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == lhsGenericDef)
                        return t;
                }

                // Check interfaces
                return rhsType
                    .GetInterfaces()
                    .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == lhsGenericDef);
            }
        }
    }
}