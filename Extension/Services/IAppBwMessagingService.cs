using Extension.Models;
using Microsoft.JSInterop;

namespace Extension.Services {
    public interface IAppBwMessagingService : IObservable<string> {
        Task Initialize(string tabId);

        Task SendToBackgroundWorkerAsync<T>(ReplyMessageData<T> replyMessageData);

        [JSInvokable]
        void ReceiveMessage(string message);

        void Dispose();
    }
}
