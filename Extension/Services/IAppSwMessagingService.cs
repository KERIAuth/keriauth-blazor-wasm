using Extension.Models;
using Microsoft.JSInterop;

namespace Extension.Services
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
