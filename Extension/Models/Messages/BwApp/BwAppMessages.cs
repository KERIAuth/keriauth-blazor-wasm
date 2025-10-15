using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp {
    /// <summary>
    /// Base record for all messages sent from BackgroundWorker to App (popup/tab/sidepanel).
    /// Direction: BackgroundWorker → App
    /// </summary>
    public abstract record BwAppMessage {
        [JsonPropertyName("type")]
        public string Type { get; init; }

        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        [JsonPropertyName("payload")]
        public object? Payload { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        protected BwAppMessage(string type, string? requestId = null, object? payload = null, string? error = null) {
            Type = type;
            RequestId = requestId;
            Payload = payload;
            Error = error;
        }
    }

    /// <summary>
    /// Message types sent from BackgroundWorker to App.
    /// </summary>
    public static class BwAppMessageTypes {
        public const string LOCK_APP = "lockApp";
        public const string SYSTEM_LOCK_DETECTED = "systemLockDetected";
        public const string FROM_BACKGROUND_WORKER = "fromBackgroundWorker";
        public const string FORWARDED_MESSAGE = "forwardedMessage";
    }

    /// <summary>
    /// Message instructing the App to lock (e.g., due to inactivity timeout).
    /// </summary>
    public record BwAppLockMessage : BwAppMessage {
        public BwAppLockMessage()
            : base(BwAppMessageTypes.LOCK_APP) { }
    }

    /// <summary>
    /// Message indicating system lock/suspend/hibernate was detected.
    /// App should immediately lock to protect sensitive data.
    /// </summary>
    public record BwAppSystemLockDetectedMessage : BwAppMessage {
        public BwAppSystemLockDetectedMessage()
            : base(BwAppMessageTypes.SYSTEM_LOCK_DETECTED) { }
    }

    /// <summary>
    /// Generic message forwarded from ContentScript to App.
    /// Used for messages that originate from web pages and need to be displayed/handled in the App UI.
    /// </summary>
    public record BwAppForwardedMessage : BwAppMessage {
        public BwAppForwardedMessage(string? requestId, object? payload, string? error = null)
            : base(BwAppMessageTypes.FORWARDED_MESSAGE, requestId, payload, error) { }
    }
}
