using System.Text.Json.Serialization;

namespace Extension.Models.Messages.AppBw.Requests;

/// <summary>
/// Payload sent from App to BackgroundWorker when user approves a connection invite.
/// Contains the AID the user selected for this connection.
/// </summary>
public record ConnectionInviteReplyPayload(
    [property: JsonPropertyName("aidName")] string AidName
);
