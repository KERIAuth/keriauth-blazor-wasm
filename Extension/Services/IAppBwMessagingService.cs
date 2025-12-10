using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Microsoft.JSInterop;

namespace Extension.Services {
    public interface IAppBwMessagingService : IObservable<BwAppMessage<object>> {
        Task Initialize(string tabId);

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker (fire-and-forget).
        /// TPayload is the payload type for the message.
        /// </summary>
        Task SendToBackgroundWorkerAsync<TPayload>(AppBwMessage<TPayload> message);

        /// <summary>
        /// Sends a strongly-typed message from App to BackgroundWorker and awaits a response.
        /// TPayload is the payload type for the request message.
        /// TResponse is the expected response type from BackgroundWorker.
        /// </summary>
        Task<TResponse?> SendRequestAsync<TPayload, TResponse>(AppBwMessage<TPayload> message) where TResponse : class;

        [JSInvokable]
        void ReceiveMessage(BwAppMessage<object> message);

        void Dispose();
    }
}
