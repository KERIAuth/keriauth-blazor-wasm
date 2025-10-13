using Extension.Helper;
using Extension.Models.AppBwMessages;
using Extension.Models.BwAppMessages;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Runtime;
// using WebExtensions.Net.Scripting;

namespace Extension.Services {
    /// <summary>
    /// Concrete implementation of BwAppMessage for deserialization purposes.
    /// Used internally by AppBwMessagingService to deserialize messages from BackgroundWorker.
    /// </summary>
    internal sealed record ConcreteBwAppMessage : BwAppMessage {
        public ConcreteBwAppMessage(string type, string? requestId = null, object? payload = null, string? error = null)
            : base(type, requestId, payload, error) { }
    }

    public class AppBwMessagingService(ILogger<AppBwMessagingService> logger, IJsRuntimeAdapter jsRuntimeAdapter) : IAppBwMessagingService {
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
        /// Sends a strongly-typed message from App to BackgroundWorker.
        /// T must be a subtype of AppBwMessage.
        /// Uses MessageJsonOptions with increased MaxDepth and RecursiveDictionaryConverter
        /// to handle deeply nested credential structures while preserving CESR/SAID ordering.
        /// </summary>
        public async Task SendToBackgroundWorkerAsync<T>(T message) where T : AppBwMessage {
            logger.LogInformation("SendToBackgroundWorkerAsync type {typeName}, message type: {messageType}",
                typeof(T).Name, message.Type);

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

        [JSInvokable]
        public void ReceiveMessage(BwAppMessage message) {
            // Handle the message received from the background worker
            logger.LogInformation("AppBwMessagingService from BW: type={type}, requestId={requestId}",
                message.Type, message.RequestId);
            OnNext(message);
        }

        public void Dispose() {
            _objectReference?.Dispose();
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

        // Helper method to notify observers of an Error
        //private void NotifyError(Exception Error)
        //{
        //    foreach (var observer in observers)
        //    {
        //        observer.OnError(Error);
        //    }
        //}

        // Helper method to notify observers of completion
        //private void Complete()
        //{
        //    foreach (var observer in observers)
        //    {
        //        observer.OnCompleted();
        //    }
        //}

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
