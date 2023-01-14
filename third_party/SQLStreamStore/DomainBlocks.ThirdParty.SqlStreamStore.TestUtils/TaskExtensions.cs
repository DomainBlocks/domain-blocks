﻿namespace DomainBlocks.ThirdParty.SqlStreamStore.TestUtils
{
    using System;
    using System.Threading.Tasks;

    public static class TaskExtensions
    {
        public static async Task<T> WithTimeout<T>(this Task<T> task, int timeout = 10000) =>
            await Task.WhenAny(task, Task.Delay(timeout)) == task
                ? task.Result
                : throw new TimeoutException("Timed out waiting for task");

        public static async Task WithTimeout(this Task task, int timeout = 10000)
        {
            if(await Task.WhenAny(task, Task.Delay(timeout)) != task)
                throw new TimeoutException("Timed out waiting for task");
        }
    }
}