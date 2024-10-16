using KeriAuth.BrowserExtension.Models;
using Microsoft.JSInterop;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IAppSwMessagingService : IObservable<string>
    {
        public Task Initialize(string tabId);

        public Task SendToServiceWorkerAsync<T>(ReplyMessageData<T> replyMessageData);

        [JSInvokable]
        public void ReceiveMessage(string message);

        public void Dispose();
    }
}
