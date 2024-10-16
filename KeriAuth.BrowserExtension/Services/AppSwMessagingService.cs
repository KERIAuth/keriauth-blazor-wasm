using KeriAuth.BrowserExtension.Models;
using Microsoft.JSInterop;
using System.Text.Json;

namespace KeriAuth.BrowserExtension.Services
{
    public class AppSwMessagingService(ILogger<AppSwMessagingService> logger, IJSRuntime jsRuntime) : IAppSwMessagingService
    {
        private readonly List<IObserver<string>> observers = [];
        private IJSObjectReference? _port;
        private IJSObjectReference _interopModule = default!;
        private DotNetObjectReference<AppSwMessagingService> _objectReference = default!;

        public async Task Initialize(string tabId)
        {
            try
            {
                _objectReference = DotNetObjectReference.Create(this);
                _interopModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/SwAppInterop.js");

                if (_interopModule != null)
                {
                    logger.LogInformation("JS module SwAppInterop.js import was successful.");
                    await jsRuntime.InvokeVoidAsync("console.log", "test log");

                    _port = await _interopModule.InvokeAsync<IJSObjectReference>("SwAppInteropModule.initializeMessaging", _objectReference, "tab2");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to import JS module: {ex.Message}");
            }
        }

        public async Task SendToServiceWorkerAsync<T>(ReplyMessageData<T> replyMessageData)
        {
            logger.LogInformation("SendToServiceWorkerAsync type {r}{n}", typeof(T).Name, replyMessageData.PayloadTypeName);

            if (_port != null)
            {
                var replyJson = JsonSerializer.Serialize(replyMessageData);
                logger.LogInformation("SendToServiceWorkerAsync sending payloadJson: {p}", replyJson);
                await _interopModule.InvokeVoidAsync("SwAppInteropModule.sendMessageToServiceWorker", _port, replyJson);
                logger.LogInformation("SendToServiceWorkerAsync to SW: sent");
            }
            else
            {
                logger.LogError("Port is null");
            }
        }

        [JSInvokable]
        public void ReceiveMessage(string message)
        {
            // Handle the message received from the service worker
            logger.LogInformation("AppSwMessagineService from SW: {m}", message);
            OnNext(message);
        }

        public void Dispose()
        {
            _objectReference?.Dispose();
        }

        public IDisposable Subscribe(IObserver<string> observer)
        {
            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }
            return new Unsubscriber(observers, observer);
        }



        private void OnNext(string value)
        {
            Console.WriteLine($"Received: {value}");
            foreach (var observer in observers)
            {
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
        private class Unsubscriber(List<IObserver<string>> observers, IObserver<string> observer) : IDisposable
        {
            public void Dispose()
            {
                if (observer != null && observers.Contains(observer))
                {
                    observers.Remove(observer);
                }
            }
        }
    }
}
