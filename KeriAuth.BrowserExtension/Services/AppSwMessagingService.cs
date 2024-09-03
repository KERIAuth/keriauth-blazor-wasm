using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using System.Threading.Tasks;

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
            _objectReference = DotNetObjectReference.Create(this);
            _interopModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./scripts/es6/SwAppInterop.js");
            // await js.InvokeVoidAsync("registerServiceWorkerMessaging");
            _port = await _interopModule.InvokeAsync<IJSObjectReference>("SwAppInteropModule.initializeMessaging", _objectReference, "tab2");
        }

        public async Task SendToServiceWorkerAsync<T>(string type, string message, T payload)
        {
            if (_port != null)
            {
                // TODO P2 make the message payload typed
                //var messagePayload = new MessagePayload<T>
                //{
                //    Message = message,
                //    Payload = payload
                //};

                logger.LogInformation("AppSwMessagingService to SW: sending type, message, payload: {t} {m} {p}", type, message, payload);

                //var messageJson = JsonSerializer.Serialize(message);
                //var messageBytes = Encoding.UTF8.GetBytes(messageJson);
                await _interopModule.InvokeVoidAsync("SwAppInteropModule.sendMessageToServiceWorker", _port, type, message);
                // await Task.Delay(1000); // TODO big issue here?, need to wait for the message to be sent.  See mingyaulee for a better way to do this?
                logger.LogInformation("AppSwMessagingService to SW: sent");
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

        // Helper method to notify observers of an error
        //private void NotifyError(Exception error)
        //{
        //    foreach (var observer in observers)
        //    {
        //        observer.OnError(error);
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
