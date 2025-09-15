using FluentResults;

namespace Extension.Models;

public record StorageError(string Message, Exception? InnerException = null) : IError {
    public Dictionary<string, object> Metadata => [];
    public List<IError> Reasons => InnerException != null ? [new ExceptionalError(InnerException)] : [];
}

public record ValidationError(string Field, string Message) : IError {
    public List<IError> Reasons => [];
    public Dictionary<string, object> Metadata => new() { ["Field"] = Field };
}

public record ConnectionError(string Endpoint, string Reason, Exception? InnerException = null) : IError {
    public string Message => $"Failed to connect to {Endpoint}: {Reason}";
    public List<IError> Reasons => InnerException != null ? [new ExceptionalError(InnerException)] : [];
    public Dictionary<string, object> Metadata => new() { ["Endpoint"] = Endpoint };
}

public record StateTransitionError(string FromState, string ToState, string Reason) : IError {
    public string Message => $"Cannot transition from {FromState} to {ToState}: {Reason}";
    public List<IError> Reasons => [];
    public Dictionary<string, object> Metadata => new() { ["FromState"] = FromState, ["ToState"] = ToState };
}

public record JavaScriptInteropError(string Operation, string Details, Exception? InnerException = null) : IError {
    public string Message => $"JavaScript interop failed during {Operation}: {Details}";
    public List<IError> Reasons => InnerException != null ? [new ExceptionalError(InnerException)] : [];
    public Dictionary<string, object> Metadata => new() { ["Operation"] = Operation };
}

public record AuthenticationError(string Reason) : IError {
    public string Message => $"Authentication failed: {Reason}";
    public List<IError> Reasons => [];
    public Dictionary<string, object> Metadata => [];
}

public record ConfigurationError(string Component, string Issue) : IError {
    public string Message => $"Configuration error in {Component}: {Issue}";
    public List<IError> Reasons => [];
    public Dictionary<string, object> Metadata => new() { ["Component"] = Component };
}

public record OperationTimeoutError(string Operation, int TimeoutSeconds) : IError {
    public string Message => $"Operation '{Operation}' timed out after {TimeoutSeconds} seconds";
    public List<IError> Reasons => [];
    public Dictionary<string, object> Metadata => new() { ["Operation"] = Operation, ["TimeoutSeconds"] = TimeoutSeconds };
}

public static class ResultExtensions {
    public static Result<T> ToTypedResult<T>(this Result<T> result, Func<string, IError> errorFactory) {
        if (result.IsSuccess) return result;
        
        var errors = result.Errors.Select(e => errorFactory(e.Message)).ToList();
        return Result.Fail<T>(errors);
    }
    
    public static async Task<Result<TNext>> BindAsync<T, TNext>(this Task<Result<T>> resultTask, Func<T, Task<Result<TNext>>> next) {
        var result = await resultTask;
        if (result.IsFailed) return Result.Fail<TNext>(result.Errors);
        return await next(result.Value);
    }
}