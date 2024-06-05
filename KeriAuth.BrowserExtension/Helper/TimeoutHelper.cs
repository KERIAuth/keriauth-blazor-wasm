using FluentResults;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KeriAuth.BrowserExtension.Helper;

public static class TimeoutHelper
{
    public static async Task<Result<T>> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout)
    {
        // ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);
        // var timeout = new TimeSpan(0, 0, 0, 0, timeoutMs);
        using var cts = new CancellationTokenSource();
        var timeoutTask = Task.Delay(timeout, cts.Token);
        var operationTask = operation(cts.Token);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            return Result.Fail<T>("Operation timed out.");
        }
        else
        {
            cts.Cancel(); // Cancel the timeout task
            try
            {
                var result = await operationTask;
                return Result.Ok(result);
            }
            catch (Exception ex)
            {
                return Result.Fail<T>(ex.Message);
            }
        }
    }
}