﻿using System.Diagnostics;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace DomainBlocks.ThirdParty.SqlStreamStore.Imports.Ensure.That
{
    public static partial class EnsureArg
    {
        [DebuggerStepThrough]
        public static void IsNotNull<T>(T? value, string paramName = Param.DefaultName) where T : struct
        {
            if (!Ensure.IsActive)
                return;

            if (value == null)
                throw new ArgumentNullException(paramName, ExceptionMessages.Common_IsNotNull_Failed);
        }
    }
}