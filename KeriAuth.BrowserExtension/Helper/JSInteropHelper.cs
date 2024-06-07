using FluentResults;
using Microsoft.JSInterop;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KeriAuth.BrowserExtension.Helper
{
    public class JSInteropHelper(IJSRuntime jsRuntime)
    {
        // TODO set shorter timeout depending on #debug

#if DEBUG
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(22222000);
#else
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(5000);
#endif

        private readonly IJSRuntime _jsRuntime = jsRuntime;

        // Adds parameters to control the error message detail
        public async Task<Result<T>> InvokeAsync<T>(
            string functionName,
            string errorMessage,
            TimeSpan? timeout = null,
            bool includeExceptionMessage = false,
            bool includeNestedExceptionMessages = false,
            params object[] args)
        {
            var to = timeout ?? DefaultTimeout;
            try
            {
                Debug.Assert(_jsRuntime is not null);

                // Check if T is among the allowed types
                Debug.Assert(

                        typeof(T) == typeof(int) || // number (TypeScript)
                        typeof(T) == typeof(string) || // string (TypeScript)
                        typeof(T) == typeof(bool) || // boolean (TypeScript)
                        typeof(T) == typeof(double) || // number (TypeScript)
                        typeof(T) == typeof(float) || // number (TypeScript), but be cautious with precision
                        typeof(T) == typeof(long) || // number (TypeScript), but be cautious with the range
                        typeof(T) == typeof(object) || // any or object (TypeScript), use specific types for better type safety
                        typeof(T) == typeof(JsonElement) || // any (TypeScript), for handling arbitrary JSON objects
                                                            // JsonObject isn't directly recognized in System.Text.Json. For dynamic JSON objects, you might use 'dynamic' in C# to represent 'any' in TypeScript.
                        typeof(T).IsArray || // Array<Type> or Type[] (TypeScript)
                        typeof(T).IsGenericType && (
                            typeof(T).GetGenericTypeDefinition() == typeof(List<>) || // Array<Type> (TypeScript)
                            typeof(T).GetGenericTypeDefinition() == typeof(Dictionary<,>) || // { [key: Type]: Type } (TypeScript), for key-value pairs
                            typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>) // Type | null | undefined (TypeScript), for nullable value types
                        ) ||
                        typeof(T) == typeof(Task) // void (TypeScript), for functions that don't return a value
                    ,
                    "InvokeAsyncWithCheck called with unsupported type parameter."
                );
                var result = await _jsRuntime.InvokeAsync<T>(functionName, to, args);
                return Result.Ok(result);
            }
            catch (OperationCanceledException)
            {
                var timeoutErrorMessage = $"The operation was canceled because it exceeded the timeout of {timeout?.TotalSeconds} seconds.";
                return Result.Fail(timeoutErrorMessage);
            }
            catch (JSException jsEx)
            {
                if (jsEx is null)
                    return Result.Fail<T>(new FluentResults.Error("unexpected null javascript exception"));

                var detailedErrorMessage = errorMessage + jsEx.Message; //  BuildErrorMessage(errorMessage, jsEx, includeExceptionMessage, includeNestedExceptionMessages);
                                                                        // Log the detailed error message if necessary
                Console.WriteLine(detailedErrorMessage);
                return Result.Fail(detailedErrorMessage);
            }
            catch (Exception ex)
            {
                var detailedErrorMessage = BuildErrorMessage(errorMessage, ex, includeExceptionMessage, includeNestedExceptionMessages);
                // Log the detailed error message if necessary
                Console.WriteLine(detailedErrorMessage);
                return Result.Fail(detailedErrorMessage);
            }
        }

        // An example usage of the above:
        // Javascript:
        //function getRandomInt(min, max)
        //{
        //    min = Math.ceil(min);
        //    max = Math.floor(max);
        //    return Math.floor(Math.random() * (max - min + 1)) + min;
        //}
        //
        // C#:
        // @inject IJSRuntime js
        //protected override async Task OnInitializedAsync()
        //{
        //    var jsInteropHelper = new JSInteropHelper(JSRuntime);
        //    var min = 1;
        //    var max = 100;
        //    var result = await jsInteropHelper.InvokeAsync<int>("getRandomInt", "Failed to generate a random integer", min, max);

        //    if (result.IsSuccess)
        //    {
        //        Console.WriteLine($"Random integer: {result.Value}");
        //    }
        //    else
        //    {
        //        Console.WriteLine(result.Errors[0].Message);
        //    }
        //}

        private static string BuildErrorMessage(string baseMessage, Exception exception, bool includeExceptionMessage, bool includeNestedExceptionMessages)
        {
            if (!includeExceptionMessage && !includeNestedExceptionMessages) return baseMessage;

            var sb = new StringBuilder(baseMessage);
            if (includeExceptionMessage)
            {
                sb.Append(" Exception: ").Append(exception.Message);
            }

            if (includeNestedExceptionMessages && exception.InnerException != null)
            {
                var innerException = exception.InnerException;
                while (innerException != null)
                {
                    sb.Append(" Inner Exception: ").Append(innerException.Message);
                    innerException = innerException.InnerException;
                }
            }

            return sb.ToString();
        }
    }
}