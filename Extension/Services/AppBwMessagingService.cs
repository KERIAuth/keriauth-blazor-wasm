using Extension.Models;
using JsBind.Net;
using Microsoft.JSInterop;
using System.Text.Json;
using WebExtensions.Net;
using WebExtensions.Net.Runtime;
// using WebExtensions.Net.Scripting;

namespace Extension.Services {
    public class AppBwMessagingService(ILogger<AppBwMessagingService> logger, IJsRuntimeAdapter jsRuntimeAdapter) : IAppBwMessagingService {
        private readonly List<IObserver<string>> observers = [];
        private DotNetObjectReference<AppBwMessagingService> _objectReference = default!;
        private int? _currentTabId;
        private WebExtensionsApi _webExtensionsApi = default!;
        private const string FromBackgroundWorkerMessageType = "fromBackgroundWorker";

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

                // Deserialize and check message type
                var messageJson = JsonSerializer.Serialize(messageObj);
                var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageJson);

                if (message != null && message.TryGetValue("type", out var typeElement)) {
                    var messageType = typeElement.GetString();

                    // Handle messages from BackgroundWorker
                    if (messageType == FromBackgroundWorkerMessageType) {
                        if (message.TryGetValue("data", out var dataElement)) {
                            var data = dataElement.GetString();
                            if (!string.IsNullOrEmpty(data)) {
                                ReceiveMessage(data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error handling message from BackgroundWorker");
            }
        }

        // TODO P2 Types for SendToBackgroundWorkerAsync should be enumerated and explicit.  Might be similar to 
        public async Task SendToBackgroundWorkerAsync<T>(ReplyMessageData<T> replyMessageData) {
            logger.LogInformation("SendToBackgroundWorkerAsync type {r}{n}", typeof(T).Name, replyMessageData.PayloadTypeName);

            try {
                // Serialize the replyMessageData (which has JsonPropertyName attributes for camelCase)
                var replyJson = JsonSerializer.Serialize(replyMessageData);

                // Parse to add tabId field
                var messageDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(replyJson);
                if (messageDict != null) {
                    messageDict["tabId"] = JsonSerializer.SerializeToElement(_currentTabId);
                    replyJson = JsonSerializer.Serialize(messageDict);
                }

                logger.LogInformation("SendToBackgroundWorkerAsync sending message with tabId {tabId}: {json}", _currentTabId, replyJson);

                // Deserialize back to object for sending
                var messageToSend = JsonSerializer.Deserialize<object>(replyJson);

                // Send to BackgroundWorker using WebExtensions.Net
                // BackgroundWorker will forward to ContentScript based on tabId in message
                await _webExtensionsApi.Runtime.SendMessage(messageToSend);

                logger.LogInformation("SendToBackgroundWorkerAsync sent (message includes tabId: {tabId})", _currentTabId);
            }
            catch (Exception ex) {
                logger.LogError(ex, "Error sending message to BackgroundWorker");
                throw;
            }
        }

        [JSInvokable]
        public void ReceiveMessage(string message) {
            // Handle the message received from the background worker
            logger.LogInformation("AppBwMessagingService from BW: {m}", message);
            OnNext(message);
        }

        public void Dispose() {
            _objectReference?.Dispose();
        }

        public IDisposable Subscribe(IObserver<string> observer) {
            if (!observers.Contains(observer)) {
                observers.Add(observer);
            }
            return new Unsubscriber(observers, observer);
        }

        private void OnNext(string value) {
            Console.WriteLine($"Received: {value}");
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
        private sealed class Unsubscriber(List<IObserver<string>> observers, IObserver<string> observer) : IDisposable {
            public void Dispose() {
                if (observer != null && observers.Contains(observer)) {
                    observers.Remove(observer);
                }
            }
        }
    }
}
