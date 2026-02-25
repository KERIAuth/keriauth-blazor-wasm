using System.Text.Json.Serialization;

namespace Extension.Models.Messages.BwApp.Requests;

/// <summary>
/// Payload sent from BackgroundWorker to App after the page confirms (or fails)
/// the mutual connection. App uses this to update its connections UI.
/// </summary>
public record ConnectionConfirmedPayload(
    [property: JsonPropertyName("oobi")] string Oobi,
    [property: JsonPropertyName("error")] string? Error = null
);
