using Extension.Helper;
using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Runtime;

namespace Extension.Services {
    /// <summary>
    /// Concrete implementation of BwAppMessage for deserialization purposes.
    /// Used internally by AppBwMessagingService to deserialize messages from BackgroundWorker.
    /// </summary>
    internal sealed record ConcreteBwAppMessage : BwAppMessage<object> {
        public ConcreteBwAppMessage(string type, string? requestId = null, object? payload = null, string? error = null)
            : base(type, requestId, payload, error) { }
    }

    public class AppBwMessagingService(ILogger<AppBwMessagingService> logger, IJsRuntimeAdapter jsRuntimeAdapter) : IAppBwMessagingService, IDisposable {
        private readonly List<IObserver<BwAppMessage<object>>> observers = [];
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

                // Get current runtime context to determine the appropriate context type
                var contextFilter = new ContextFilter() { ContextTypes = [ContextType.POPUP, ContextType.TAB, ContextType.SIDEPANEL] };
                var contexts = await _webExtensionsApi.Runtime.GetContexts(contextFilter);



                // Find the current context (should be this instance)
                var currentContext = contexts.FirstOrDefault();
                string contextType = currentContext?.ContextType.ToString() ?? "UNKNOWN";






                logger.LogInformation("Initializing messaging for context type: {contextType}", contextType);

                // Set up listener for messages from BackgroundWorker using WebExtensions.Net
                _webExtensionsApi.Runtime.OnMessage.AddListener(OnMessageFromBackgroundWorker);

                logger.LogInformation("AppBwMessagingService: Message listener initialized for {contextType}", contextType);
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

                    // Extract payload (could be complex object)
                    object? payload = null;
                    if (messageDict.TryGetValue("payload", out var payloadElem) && payloadElem.ValueKind != JsonValueKind.Null) {
                        var payloadJson = payloadElem.GetRawText();
                        payload = JsonSerializer.Deserialize<object>(payloadJson, MessageJsonOptions);
                    }

                    if (!string.IsNullOrEmpty(type)) {
                        // Create a concrete BwAppMessage instance
                        var bwAppMessage = new ConcreteBwAppMessage(type, requestId, payload, error);
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
        /// </summary>
        public async Task<TResponse?> SendRequestAsync<TPayload, TResponse>(AppBwMessage<TPayload> message) where TResponse : class {
            logger.LogInformation("SendRequestAsync type {typeName}, message type: {messageType}, expecting response type: {responseType}",
                typeof(AppBwMessage<TPayload>).Name, message.Type, typeof(TResponse).Name);

            try {
                // Serialize the strongly-typed AppBwMessage with increased depth and RecursiveDictionary support
                var messageJson = JsonSerializer.Serialize(message, MessageJsonOptions);

                logger.LogInformation("SendRequestAsync sending message with tabId {tabId}: {json}",
                    message.TabId, messageJson);

                // Deserialize back to object for sending via WebExtensions API
                var messageToSend = JsonSerializer.Deserialize<object>(messageJson, MessageJsonOptions);

                // Send to BackgroundWorker and await response
                // The browser-polyfill wraps Chrome's sendMessage to support Promise returns from onMessage
                var response = await _webExtensionsApi.Runtime.SendMessage(messageToSend);

                logger.LogInformation("SendRequestAsync received response: {response}", response);

                if (response.ValueKind == JsonValueKind.Null || response.ValueKind == JsonValueKind.Undefined) {
                    logger.LogWarning("SendRequestAsync received null/undefined response");
                    return null;
                }

                // Deserialize the response to the expected type
                var responseJson = response.GetRawText();
                var typedResponse = JsonSerializer.Deserialize<TResponse>(responseJson, MessageJsonOptions);

                logger.LogInformation("SendRequestAsync deserialized response to {responseType}: {response}",
                    typeof(TResponse).Name, responseJson);

                return typedResponse;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error sending request to BackgroundWorker");
                throw;
            }
        }

        [JSInvokable]
        public void ReceiveMessage(BwAppMessage<object> message) {
            // Handle the message received from the background worker
            logger.LogInformation("AppBwMessagingService from BW: type={type}, requestId={requestId}",
                message.Type, message.RequestId);
            OnNext(message);
        }

        public IDisposable Subscribe(IObserver<BwAppMessage<object>> observer) {
            if (!observers.Contains(observer)) {
                observers.Add(observer);
            }
            return new Unsubscriber(observers, observer);
        }

        private void OnNext(BwAppMessage<object> value) {
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
        private sealed class Unsubscriber(List<IObserver<BwAppMessage<object>>> observers, IObserver<BwAppMessage<object>> observer) : IDisposable {
            public void Dispose() {
                if (observer != null && observers.Contains(observer)) {
                    observers.Remove(observer);
                }
            }
        }
    }
}
