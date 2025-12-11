using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload for BW→App NotifyUserOfUpdate request.
/// Contains details about the extension update for user notification.
/// </summary>
public record RequestNotifyUserOfUpdatePayload(
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("previousVersion")] string PreviousVersion,
    [property: JsonPropertyName("currentVersion")] string CurrentVersion,
    [property: JsonPropertyName("timestamp")] string Timestamp
);

/// <summary>
/// Request from BackgroundWorker to App to notify user of extension update.
/// Displayed on the NewRelease page to inform user about the update.
/// </summary>
public record BwAppRequestNotifyUserOfUpdateMessage : BwAppMessage<RequestNotifyUserOfUpdatePayload> {
    public BwAppRequestNotifyUserOfUpdateMessage(string requestId, RequestNotifyUserOfUpdatePayload payload)
        : base(BwAppMessageType.RequestNotifyUserOfUpdate, requestId, payload) { }
}
