using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Microsoft.JSInterop;

namespace Extension.Services {
    public interface IAppBwMessagingService : IObservable<BwAppMessage<object>> {
        Task Initialize(string tabId);

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker.
        /// TPayload is the payload type for the message.
        /// </summary>
        Task SendToBackgroundWorkerAsync<TPayload>(AppBwMessage<TPayload> message);

        [JSInvokable]
        void ReceiveMessage(BwAppMessage<object> message);

        void Dispose();
    }
}
