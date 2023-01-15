using System.Diagnostics;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That
{
    public static partial class EnsureArg
    {
        [DebuggerStepThrough]
        public static void IsNotEmpty(Guid value, string paramName = Param.DefaultName)
        {
            if (!Ensure.IsActive)
                return;

            if (value.Equals(Guid.Empty))
                throw new ArgumentException(
                    ExceptionMessages.Guids_IsNotEmpty_Failed,
                    paramName);
        }
    }
}