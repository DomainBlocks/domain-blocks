using System.Diagnostics;
using JetBrains.Annotations;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That
{
    public static class Ensure
    {
        public static bool IsActive { get; private set; } = true;

        public static void Off() => IsActive = false;

        public static void On() => IsActive = true;

        [DebuggerStepThrough]
        public static Param<T> That<T>([NoEnumeration]T value, string name = Param.DefaultName) => new Param<T>(name, value);

        [DebuggerStepThrough]
        public static Param<T> That<T>(Func<T> expression, string name = Param.DefaultName) => new Param<T>(
            name,
            expression.Invoke());

        [DebuggerStepThrough]
        public static TypeParam ThatTypeFor<T>(T value, string name = Param.DefaultName) => new TypeParam(name, value.GetType());
    }
}