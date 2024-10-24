using FluentResults;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KeriAuth.BrowserExtension.Services
{
    public class HttpClientService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HttpClientService> logger;

        public HttpClientService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            logger = new Logger<HttpClientService>(new LoggerFactory()); // TODO P4 insert via DI

            // Define a timeout policy that times out after 2 seconds.
            var timeoutDuration = TimeSpan.FromSeconds(2);
#if DEBUG
            timeoutDuration = TimeSpan.FromSeconds(10);
#endif
            var timeoutPolicy = Policy.TimeoutAsync(timeoutDuration, TimeoutStrategy.Optimistic);

            // Define a retry policy that retries up to 3 times with an exponential backoff.
            AsyncRetryPolicy retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>() // Treat timeout as a reason for retry
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            // logger.LogInformation($"Request failed with {response.Exception?.Message ?? response.Result.StatusCode.ToString()}. Retry attempt: {retryCount}");
        }

        public async Task<Result<TResponse>> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest? content = default)
        {
            try
            {
                HttpContent? httpContent = null;
                if (content != null && (method == HttpMethod.Post || method == HttpMethod.Put))
                {
                    var json = JsonSerializer.Serialize(content);
                    httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var request = new HttpRequestMessage(method, url) { Content = httpContent };

                var _retryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .RetryAsync(3, onRetry: (exception, retryCount, context) =>
                    {
                        // This is an optional callback you can use to log retry attempts
                        logger.LogInformation("Retry {RetryCount} of {Context.PolicyKey} at {Context.OperationKey}, due to: {Exception}.", retryCount, context.PolicyKey, context.OperationKey, exception);
                    });

                var response = await _retryPolicy.ExecuteAsync(() => _httpClient.SendAsync(request));

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<TResponse>(responseContent);
                    Debug.Assert(result != null, nameof(result) + " != null");
                    return result.ToResult<TResponse>();
                }
                else
                {
                    return Result.Fail($"Request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred: {ex.Message}");
            }
        }

        public async Task<Result<HttpResponseMessage>> GetJsonAsync<TResponse>(string url)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(3);  // TODO P3 Consider making this configurable and also providing an external cts Source. Apply pattern elsewhere.
            HttpResponseMessage httpResponseMessage;
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                httpResponseMessage = await _httpClient.GetAsync(url, cts.Token);
                Debug.Assert(httpResponseMessage is not null);
                return httpResponseMessage.ToResult();
            }
            catch (Exception ex)
            {
                return Result.Fail($"An exception occurred: {ex.Message}");
            }
        }
    }
}
