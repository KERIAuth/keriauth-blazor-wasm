using FluentResults;

namespace Extension.Helper;

public static class TimeoutHelper
{
    /* 
    * Wraps an asynchronous operation with a timeout. If the operation does not complete within the specified timeout, it returns a failure result.
    * Otherwise, it returns the result of the operation.
    */
    public static async Task<Result<T>> WithTimeout<T>(Func<CancellationToken, Task<T>> operation, TimeSpan timeout) {
        // Create separate cancellation tokens for timeout and operation
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var operationCts = new CancellationTokenSource();
        
        // Link them so cancelling timeout also cancels operation
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, operationCts.Token);
        
        try {
            var operationTask = operation(linkedCts.Token);
            var result = await operationTask.ConfigureAwait(false);
            return Result.Ok(result);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested) {
            // Timeout occurred - cancel the operation
            operationCts.Cancel();
            return Result.Fail<T>("Operation timed out.");
        }
        catch (OperationCanceledException) {
            // Operation was cancelled for another reason
            return Result.Fail<T>("Operation was cancelled.");
        }
        catch (Exception ex) {
            return Result.Fail<T>(ex.Message);
        }
    }
}