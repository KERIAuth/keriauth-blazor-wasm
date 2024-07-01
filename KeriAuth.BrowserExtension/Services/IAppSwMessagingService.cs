using Microsoft.JSInterop;
using System.Text.Json;
using System.Text;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IAppSwMessagingService : IObservable<string>
    {
        public Task Initialize(string tabId);

        public Task SendToServiceWorkerAsync<T>(string message, T payload);

        [JSInvokable]
        public void ReceiveMessage(string message);

        public void Dispose();
    }
}
