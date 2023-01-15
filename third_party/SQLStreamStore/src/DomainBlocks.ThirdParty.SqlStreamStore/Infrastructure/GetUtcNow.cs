﻿namespace DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure
{
    /// <summary>
    ///     Represents an operation to get the UTC data time. Mainly used in tests to control the temporal concerns.
    /// </summary>
    /// <returns>
    ///     A <see cref="DateTime"/> representing the current UTC date and time.
    /// </returns>
    public delegate DateTime GetUtcNow();
}