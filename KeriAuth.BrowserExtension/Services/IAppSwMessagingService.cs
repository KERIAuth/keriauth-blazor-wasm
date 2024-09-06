using Microsoft.JSInterop;
using System.Text.Json;
using System.Text;
using KeriAuth.BrowserExtension.Models;

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
