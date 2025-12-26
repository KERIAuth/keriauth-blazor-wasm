using System.Text.Json.Serialization;
using Extension.Models.Messages.Common;

namespace Extension.Models.Messages.BwApp {
    /// <summary>
    /// Message sent from BackgroundWorker to App (popup/tab/sidepanel).
    /// Direction: BackgroundWorker → App
    /// Non-generic version for convenience when constructing messages with untyped data.
    /// For initial deserialization when type is unknown, use FromBwMessage directly.
    /// </summary>
    public record BwAppMessage : FromBwMessage<object> {
        public BwAppMessage(string type, string? requestId = null, object? data = null, string? error = null)
            : base(type, requestId, data, error) { }

        /// <summary>
        /// Constructor for compile-time type-safe message creation.
        /// </summary>
        public BwAppMessage(BwAppMessageType type, string? requestId = null, object? data = null, string? error = null)
            : this(type.Value, requestId, data, error) { }
    }

    /// <summary>
    /// Message sent from BackgroundWorker to App (popup/tab/sidepanel) with typed payload.
    /// Direction: BackgroundWorker → App
    /// </summary>
    public record BwAppMessage<T> : FromBwMessage<T> {
        /// <summary>
        /// Constructor for JSON deserialization.
        /// </summary>
        [JsonConstructor]
        public BwAppMessage(string type, string? requestId = null, T? data = default, string? error = null)
            : base(type, requestId, data, error) { }

        /// <summary>
        /// Constructor for compile-time type-safe message creation.
        /// </summary>
        public BwAppMessage(BwAppMessageType type, string? requestId = null, T? data = default, string? error = null)
            : this(type.Value, requestId, data, error) { }
    }

    /// <summary>
    /// Strongly-typed message type that must be one of the defined values.
    /// Use the static properties (e.g., BwAppMessageType.LockApp) for compile-time safety.
    /// Use the Values nested class for switch case labels (e.g., BwAppMessageType.Values.LockApp).
    /// </summary>
    public readonly struct BwAppMessageType : IEquatable<BwAppMessageType> {
        /// <summary>
        /// Constant string values for use in switch case labels.
        /// </summary>
        public static class Values {
            public const string LockApp = "BwApp.LockApp";
            public const string SystemLockDetected = "BwApp.SystemLockDetected";
            public const string FromBackgroundWorker = "BwApp.FromBackgroundWorker";
            public const string ForwardedMessage = "BwApp.ForwardedMessage";

            // BW→App request types (require App UI interaction and response)
            /// <summary>Request App to show SelectAuthorize UI for user to select an identifier.</summary>
            public const string RequestSelectAuthorize = "BwApp.RequestSelectAuthorize";
            /// <summary>Request App to show SignHeaders UI for user to approve signing.</summary>
            public const string RequestSignHeaders = "BwApp.RequestSignHeaders";
            /// <summary>Request App to show SignData UI for user to select identifier and sign data.</summary>
            public const string RequestSignData = "BwApp.RequestSignData";
            /// <summary>Request App to show CreateCredential UI for user to approve data attestation credential creation.</summary>
            public const string RequestCreateCredential = "BwApp.RequestCreateCredential";
            /// <summary>Request App to notify user of extension update.</summary>
            public const string RequestNotifyUserOfUpdate = "BwApp.RequestNotifyUserOfUpdate";
        }

        public string Value { get; }

        private BwAppMessageType(string value) => Value = value;

        // Static properties for compile-time type safety
        public static BwAppMessageType LockApp { get; } = new(Values.LockApp);
        public static BwAppMessageType SystemLockDetected { get; } = new(Values.SystemLockDetected);
        public static BwAppMessageType FromBackgroundWorker { get; } = new(Values.FromBackgroundWorker);
        public static BwAppMessageType ForwardedMessage { get; } = new(Values.ForwardedMessage);
        public static BwAppMessageType RequestSelectAuthorize { get; } = new(Values.RequestSelectAuthorize);
        public static BwAppMessageType RequestSignHeaders { get; } = new(Values.RequestSignHeaders);
        public static BwAppMessageType RequestSignData { get; } = new(Values.RequestSignData);
        public static BwAppMessageType RequestCreateCredential { get; } = new(Values.RequestCreateCredential);
        public static BwAppMessageType RequestNotifyUserOfUpdate { get; } = new(Values.RequestNotifyUserOfUpdate);

        /// <summary>
        /// Parse a string value into a BwAppMessageType.
        /// Use this when deserializing from JSON or other external sources.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the value is not a valid message type.</exception>
        public static BwAppMessageType Parse(string value) {
            if (TryParse(value, out var result)) {
                return result;
            }
            throw new ArgumentException($"Invalid BwAppMessageType: '{value}'", nameof(value));
        }

        /// <summary>
        /// Try to parse a string value into a BwAppMessageType.
        /// Returns true if successful, false if the value is not a valid message type.
        /// </summary>
        public static bool TryParse(string? value, out BwAppMessageType result) {
            result = default;
            if (value is null) return false;

            switch (value) {
                case Values.LockApp:
                    result = LockApp;
                    return true;
                case Values.SystemLockDetected:
                    result = SystemLockDetected;
                    return true;
                case Values.FromBackgroundWorker:
                    result = FromBackgroundWorker;
                    return true;
                case Values.ForwardedMessage:
                    result = ForwardedMessage;
                    return true;
                case Values.RequestSelectAuthorize:
                    result = RequestSelectAuthorize;
                    return true;
                case Values.RequestSignHeaders:
                    result = RequestSignHeaders;
                    return true;
                case Values.RequestSignData:
                    result = RequestSignData;
                    return true;
                case Values.RequestCreateCredential:
                    result = RequestCreateCredential;
                    return true;
                case Values.RequestNotifyUserOfUpdate:
                    result = RequestNotifyUserOfUpdate;
                    return true;
                default:
                    return false;
            }
        }

        public static implicit operator string(BwAppMessageType type) => type.Value;
        public override string ToString() => Value;
        public bool Equals(BwAppMessageType other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is BwAppMessageType other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public static bool operator ==(BwAppMessageType left, BwAppMessageType right) => left.Equals(right);
        public static bool operator !=(BwAppMessageType left, BwAppMessageType right) => !left.Equals(right);
    }

    /// <summary>
    /// Message instructing the App to lock (e.g., due to inactivity timeout).
    /// </summary>
    public record BwAppLockMessage : BwAppMessage<object> {
        public BwAppLockMessage()
            : base(BwAppMessageType.LockApp) { }
    }

    /// <summary>
    /// Message indicating system lock/suspend/hibernate was detected.
    /// App should immediately lock to protect sensitive data.
    /// </summary>
    public record BwAppSystemLockDetectedMessage : BwAppMessage<object> {
        public BwAppSystemLockDetectedMessage()
            : base(BwAppMessageType.SystemLockDetected) { }
    }

    /// <summary>
    /// Generic message forwarded from ContentScript to App.
    /// Used for messages that originate from web pages and need to be displayed/handled in the App UI.
    /// </summary>
    public record BwAppForwardedMessage : BwAppMessage<object> {
        public BwAppForwardedMessage(string? requestId, object? data, string? error = null)
            : base(BwAppMessageType.ForwardedMessage, requestId, data, error) { }
    }
}
