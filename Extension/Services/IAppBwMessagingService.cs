using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Microsoft.JSInterop;

namespace Extension.Services {
    public interface IAppBwMessagingService : IObservable<BwAppMessage> {
        Task Initialize(string tabId);

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker.
        /// T must be a subtype of AppBwMessage.
        /// </summary>
        Task SendToBackgroundWorkerAsync<T>(T message) where T : AppBwMessage;

        [JSInvokable]
        void ReceiveMessage(BwAppMessage message);

        void Dispose();
    }
}
