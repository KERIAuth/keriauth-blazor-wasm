using Extension.Models.Messages.AppBw;
using Extension.Models.Messages.BwApp;
using Extension.Models.Messages.Common;
using FluentResults;
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
        /// Returns Result.Fail on timeout, deserialization failure, or communication error.
        /// </summary>
        /// <param name="message">The request message to send</param>
        /// <param name="timeout">Optional timeout (defaults to AppConfig.DefaultRequestTimeout)</param>
        /// <returns>Result containing the response or failure information</returns>
        Task<Result<TResponse?>> SendRequestAsync<TPayload, TResponse>(
            AppBwMessage<TPayload> message,
            TimeSpan? timeout = null) where TResponse : class, IResponseMessage;

        [JSInvokable]
        void ReceiveMessage(BwAppMessage<object> message);

        void Dispose();
    }
}
