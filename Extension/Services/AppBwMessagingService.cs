using Extension.Models;
using Microsoft.JSInterop;
using System.Text.Json;
// using WebExtensions.Net.Scripting;

namespace Extension.Services {
    public class AppBwMessagingService(ILogger<AppBwMessagingService> logger, IJSRuntime jsRuntime) : IAppBwMessagingService {
        private readonly List<IObserver<string>> observers = [];
        private IJSObjectReference? _port;
        private IJSObjectReference _interopModule = default!;
        private DotNetObjectReference<AppBwMessagingService> _objectReference = default!;

        public async Task Initialize(string tabId) {
            try {
                _objectReference = DotNetObjectReference.Create(this);
                _interopModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/SwAppInterop.js");
                if (_interopModule != null) {
                    logger.LogInformation("JS module SwAppInterop.js import was successful.");
                    // await jsRuntime.InvokeVoidAsync("console.log", "test log");
                    _port = await _interopModule.InvokeAsync<IJSObjectReference>("SwAppInteropModule.initializeMessaging", _objectReference, "tab2");
                }
            }
            catch (JSException e) {
                logger.LogError("Failed to import JS module: {e} StackTrace: {s}", e.Message, e.StackTrace);
                throw;
            }
            catch (Exception ex) {
                logger.LogError("Failed to import JS module: {e}", ex.Message);
                throw;
            }
        }

        public async Task SendToBackgroundWorkerAsync<T>(ReplyMessageData<T> replyMessageData) {
            logger.LogInformation("SendToBackgroundWorkerAsync type {r}{n}", typeof(T).Name, replyMessageData.PayloadTypeName);

            if (_port != null) {
                var replyJson = JsonSerializer.Serialize(replyMessageData);
                logger.LogInformation("SendToBackgroundWorkerAsync sending payloadJson: {p}", replyJson);
                await _interopModule.InvokeVoidAsync("SwAppInteropModule.sendMessageToBackgroundWorker", _port, replyJson);
                logger.LogInformation("SendToBackgroundWorkerAsync to BW: sent");
            }
            else {
                logger.LogError("Port is null");
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
