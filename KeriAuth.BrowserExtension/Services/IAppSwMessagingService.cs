using KeriAuth.BrowserExtension.Models;
using Microsoft.JSInterop;

namespace KeriAuth.BrowserExtension.Services
{
    public interface IAppSwMessagingService : IObservable<string>
    {
        Task Initialize(string tabId);

        Task SendToServiceWorkerAsync<T>(ReplyMessageData<T> replyMessageData);

        [JSInvokable]
        void ReceiveMessage(string message);

        void Dispose();
    }
}
