using System.Text.Json.Serialization;

namespace Extension.Models.Messages.AppBw.Requests;

/// <summary>
/// Payload sent from App to BackgroundWorker when user approves a connection invite.
/// Contains the AID the user selected, the connection name to store, and the friendly name for the outbound OOBI.
/// </summary>
public record ConnectionInviteReplyPayload(
    [property: JsonPropertyName("aidName")] string AidName,
    [property: JsonPropertyName("connectionName")] string? ConnectionName = null,
    [property: JsonPropertyName("friendlyName")] string? FriendlyName = null
);
