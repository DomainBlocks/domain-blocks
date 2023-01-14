﻿using System.Diagnostics;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That
{
    public static partial class EnsureArg
    {
        [DebuggerStepThrough]
        public static void IsNotDefault<T>(T param, string paramName = Param.DefaultName) where T : struct
        {
            if (!Ensure.IsActive)
                return;

            if (default(T).Equals(param))
                throw new ArgumentException(ExceptionMessages.ValueTypes_IsNotDefault_Failed, paramName);
        }
    }
}