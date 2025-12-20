using Extension.Helper;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using FluentResults;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Text.Json;
using WebExtensions.Net;

namespace Extension.Services {
    public class AppBwMessagingService(ILogger<AppBwMessagingService> logger, IJsRuntimeAdapter jsRuntimeAdapter) : IAppBwMessagingService, IDisposable {
        private readonly List<IObserver<BwAppMessage>> observers = [];
        private DotNetObjectReference<AppBwMessagingService> _objectReference = default!;
        private int? _currentTabId;
        private WebExtensionsApi _webExtensionsApi = default!;
        private const string FromBackgroundWorkerMessageType = "fromBackgroundWorker";

        // JsonSerializerOptions for messages with nested structures (credentials, etc.)
        // Increased MaxDepth to handle deeply nested vLEI credential structures
        // Includes RecursiveDictionaryConverter to preserve CESR/SAID field ordering
        private static readonly JsonSerializerOptions MessageJsonOptions = new() {
            MaxDepth = 128,
            Converters = { new RecursiveDictionaryConverter() }
        };

        public async Task Initialize(string tabId) {
            try {
                _objectReference = DotNetObjectReference.Create(this);
                _webExtensionsApi = new WebExtensionsApi(jsRuntimeAdapter);

                // Store tab ID for later use when sending messages
                if (int.TryParse(tabId, out var parsedTabId)) {
                    _currentTabId = parsedTabId;
                    logger.LogInformation("Current tab ID: {tabId}", _currentTabId);
                }

                /*
                // Get current runtime context to determine the appropriate context type
                var contextFilter = new ContextFilter() { ContextTypes = [ContextType.POPUP, ContextType.TAB, ContextType.SIDEPANEL] };
                var contexts = await _webExtensionsApi.Runtime.GetContexts(contextFilter);



                // Find the current context (should be this instance)
                var currentContext = contexts.FirstOrDefault();
                string contextType = currentContext?.ContextType.ToString() ?? "UNKNOWN";
                */





                // logger.LogInformation("Initializing messaging for context type: {contextType}", contextType);

                // Set up listener for messages from BackgroundWorker using WebExtensions.Net
                _webExtensionsApi.Runtime.OnMessage.AddListener(OnMessageFromBackgroundWorker);

                logger.LogInformation("AppBwMessagingService: Message listener initialized for {contextType}", App.AppContext is not null ? App.AppContext : "unknown2");
            }
            catch (Exception ex) {
                logger.LogError(ex, "Failed to initialize AppBwMessagingService");
                throw;
            }
        }

        /// <summary>
        /// Handles messages received from BackgroundWorker via chrome.runtime.onMessage
        /// </summary>
        private void OnMessageFromBackgroundWorker(object messageObj, WebExtensions.Net.Runtime.MessageSender sender, Func<object, ValueTask> sendResponse) {
            try {
                logger.LogInformation("AppBwMessagingService received message from: {url}", sender?.Url);

                // Only handle messages from the extension's BackgroundWorker
                if (sender?.Id != _webExtensionsApi.Runtime.Id) {
                    logger.LogWarning("AppBwMessagingService: Ignoring message from different extension");
                    return;
                }

                // Deserialize to BwAppMessage by extracting properties manually
                // This approach avoids constructor binding issues with derived record types
                var messageJson = JsonSerializer.Serialize(messageObj);
                var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

                if (messageDict != null) {
                    // Extract properties from dictionary
                    var type = messageDict.TryGetValue("type", out var typeElem) ? typeElem.GetString() : null;
                    var requestId = messageDict.TryGetValue("requestId", out var reqIdElem) ? reqIdElem.GetString() : null;
                    var error = messageDict.TryGetValue("error", out var errorElem) ? errorElem.GetString() : null;

                    // Extract data (could be complex object)
                    object? data = null;
                    if (messageDict.TryGetValue("data", out var dataElem) && dataElem.ValueKind != JsonValueKind.Null) {
                        var dataJson = dataElem.GetRawText();
                        data = JsonSerializer.Deserialize<object>(dataJson, MessageJsonOptions);
                    }

                    if (!string.IsNullOrEmpty(type)) {
                        // Create a BwAppMessage instance using the non-generic version
                        var bwAppMessage = new BwAppMessage(type, requestId, data, error);
                        ReceiveMessage(bwAppMessage);
                    }
                    else {
                        logger.LogWarning("AppBwMessagingService: Message missing 'type' property");
                    }
                }
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error handling message from BackgroundWorker");
            }
        }

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker (fire-and-forget).
        /// TPayload is the payload type for the message.
        /// Uses MessageJsonOptions with increased MaxDepth and RecursiveDictionaryConverter
        /// to handle deeply nested credential structures while preserving CESR/SAID ordering.
        /// </summary>
        public async Task SendToBackgroundWorkerAsync<TPayload>(AppBwMessage<TPayload> message) {
            logger.LogInformation("SendToBackgroundWorkerAsync type {typeName}, message type: {messageType}",
                typeof(AppBwMessage<TPayload>).Name, message.Type);

            try {
                // Serialize the strongly-typed AppBwMessage with increased depth and RecursiveDictionary support
                var messageJson = JsonSerializer.Serialize(message, MessageJsonOptions);

                logger.LogInformation("SendToBackgroundWorkerAsync sending message with tabId {tabId}: {json}",
                    message.TabId, messageJson);

                // Deserialize back to object for sending via WebExtensions API
                var messageToSend = JsonSerializer.Deserialize<object>(messageJson, MessageJsonOptions);

                // Send to BackgroundWorker using WebExtensions.Net
                // BackgroundWorker will forward to ContentScript based on tabId in message
                await _webExtensionsApi.Runtime.SendMessage(messageToSend);

                logger.LogInformation("SendToBackgroundWorkerAsync sent (message includes tabId: {tabId})", message.TabId);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error sending message to BackgroundWorker");
                throw;
            }
        }

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker and awaits a response.
        /// TPayload is the payload type for the request message.
        /// TResponse is the expected response type from BackgroundWorker.
        /// The browser-polyfill enables Promise returns from onMessage handlers,
        /// allowing this method to receive the BackgroundWorker's return value.
        /// Returns Result.Fail on timeout, deserialization failure, or communication error.
        /// </summary>
        /// <param name="message">The request message to send</param>
        /// <param name="timeout">Optional timeout (defaults to AppConfig.DefaultRequestTimeout)</param>
        /// <returns>Result containing the response or failure information</returns>
        public async Task<Result<TResponse?>> SendRequestAsync<TPayload, TResponse>(
            AppBwMessage<TPayload> message,
            TimeSpan? timeout = null) where TResponse : class, IResponseMessage {

            timeout ??= AppConfig.DefaultRequestTimeout;

            logger.LogInformation("SendRequestAsync type {typeName}, message type: {messageType}, expecting response type: {responseType}, timeout: {timeout}",
                typeof(AppBwMessage<TPayload>).Name, message.Type, typeof(TResponse).Name, timeout);

            try {
                // Serialize the strongly-typed AppBwMessage with increased depth and RecursiveDictionary support
                var messageJson = JsonSerializer.Serialize(message, MessageJsonOptions);

                logger.LogInformation("SendRequestAsync sending message with tabId {tabId}: {json}",
                    message.TabId, messageJson);

                // Deserialize back to object for sending via WebExtensions API
                var messageToSend = JsonSerializer.Deserialize<object>(messageJson, MessageJsonOptions);

                // Send to BackgroundWorker and await response with timeout
                // The browser-polyfill wraps Chrome's sendMessage to support Promise returns from onMessage
                using var cts = new CancellationTokenSource(timeout.Value);

                // Convert ValueTask to Task for use with Task.WhenAny
                var sendTask = _webExtensionsApi.Runtime.SendMessage(messageToSend).AsTask();
                var delayTask = Task.Delay(timeout.Value, cts.Token);
                var completedTask = await Task.WhenAny(sendTask, delayTask);

                if (completedTask != sendTask) {
                    logger.LogWarning("SendRequestAsync timed out after {timeout}", timeout);
                    return Result.Fail<TResponse?>($"Request timed out after {timeout.Value.TotalSeconds} seconds waiting for BackgroundWorker response");
                }

                // Cancel the delay task since sendTask completed
                await cts.CancelAsync();

                var response = await sendTask;

                logger.LogInformation("SendRequestAsync received response: {response}", response);

                if (response.ValueKind == JsonValueKind.Null || response.ValueKind == JsonValueKind.Undefined) {
                    logger.LogWarning("SendRequestAsync received null/undefined response");
                    return Result.Fail<TResponse?>("Received null/undefined response from BackgroundWorker");
                }

                // Deserialize the response to the expected type
                var responseJson = response.GetRawText();
                var typedResponse = JsonSerializer.Deserialize<TResponse>(responseJson, MessageJsonOptions);

                logger.LogInformation("SendRequestAsync deserialized response to {responseType}: {response}",
                    typeof(TResponse).Name, responseJson);

                return Result.Ok(typedResponse);
            }
            catch (TaskCanceledException) {
                logger.LogWarning("SendRequestAsync was cancelled");
                return Result.Fail<TResponse?>("Request was cancelled");
            }
            catch (JsonException ex) {
                logger.LogError(ex, "SendRequestAsync failed to deserialize response");
                return Result.Fail<TResponse?>($"Failed to deserialize response: {ex.Message}");
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error sending request to BackgroundWorker");
                return Result.Fail<TResponse?>($"Request failed: {ex.Message}");
            }
        }

        [JSInvokable]
        public void ReceiveMessage(BwAppMessage message) {
            // Handle the message received from the background worker
            logger.LogInformation("AppBwMessagingService from BW: type={type}, requestId={requestId}",
                message.Type, message.RequestId);
            OnNext(message);
        }

        public IDisposable Subscribe(IObserver<BwAppMessage> observer) {
            if (!observers.Contains(observer)) {
                observers.Add(observer);
            }
            return new Unsubscriber(observers, observer);
        }

        private void OnNext(BwAppMessage value) {
            logger.LogDebug("Notifying {count} observers of message type: {type}", observers.Count, value.Type);
            foreach (var observer in observers) {
                observer.OnNext(value);
            }
        }

        public void Dispose() {
            GC.SuppressFinalize(this);
        }

        // Helper method to notify observers of an Error
        private void NotifyError(Exception Error) {
            foreach (var observer in observers) {
                observer.OnError(Error);
            }
        }

        // Helper method to notify observers of completion
        private void Complete() {
            foreach (var observer in observers) {
                observer.OnCompleted();
            }
        }

        // Inner class to handle unsubscribing
        private sealed class Unsubscriber(List<IObserver<BwAppMessage>> observers, IObserver<BwAppMessage> observer) : IDisposable {
            public void Dispose() {
                if (observer != null && observers.Contains(observer)) {
                    observers.Remove(observer);
                }
            }
        }
    }
}
