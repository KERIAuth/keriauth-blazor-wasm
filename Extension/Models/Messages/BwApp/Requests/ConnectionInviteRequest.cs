using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload sent from BackgroundWorker to App when a web page requests a mutual connection.
/// BW has already resolved the page's OOBI before sending this to App.
/// App shows UI for user to approve and select which AID to use.
/// </summary>
public record ConnectionInviteRequestPayload(
    [property: JsonPropertyName("oobi")] string Oobi,
    [property: JsonPropertyName("resolvedAidPrefix")] string? ResolvedAidPrefix = null,
    [property: JsonPropertyName("resolvedAlias")] string? ResolvedAlias = null,
    [property: JsonPropertyName("tabUrl")] string? TabUrl = null
);
