using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace Extension.Services.SignifyService.Models {
    public record IpexApplyArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("attributes")] Dictionary<string, object>? Attributes = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IpexOfferArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("acdc")] Serder Acdc,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("applySaid")] string? ApplySaid = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IpexGrantArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("acdc")] Serder Acdc,
        [property: JsonPropertyName("iss")] Serder Iss,
        [property: JsonPropertyName("anc")] Serder Anc,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("agreeSaid")] string? AgreeSaid = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null,
        [property: JsonPropertyName("acdcAttachment")] string? AcdcAttachment = null,
        [property: JsonPropertyName("issAttachment")] string? IssAttachment = null,
        [property: JsonPropertyName("ancAttachment")] string? AncAttachment = null
    );

    public record IpexAgreeArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("offerSaid")] string OfferSaid,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IpexSpurnArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("spurning")] string Spurning,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IpexAdmitArgs(
        [property: JsonPropertyName("senderName")] string SenderName,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("grantSaid")] string GrantSaid,
        [property: JsonPropertyName("message")] string? Message = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record IpexExchangeResult(
        [property: JsonPropertyName("exn")] Serder Exn,
        [property: JsonPropertyName("sigs")] List<string> Sigs,
        [property: JsonPropertyName("atc")] string Atc,
        [property: JsonPropertyName("op")] Operation Op
    );

    public record IpexNotification(
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("dt")] string Dt,
        [property: JsonPropertyName("r")] string R,
        [property: JsonPropertyName("a")] OrderedDictionary A,
        [property: JsonPropertyName("e")] OrderedDictionary? E = null
    );

    public record Ipex(
        [property: JsonPropertyName("said")] string Said,
        [property: JsonPropertyName("exn")] ExchangeMessage Exn,
        [property: JsonPropertyName("atc")] string? Atc = null,
        [property: JsonPropertyName("dt")] string? Dt = null,
        [property: JsonPropertyName("route")] string? Route = null
    );
}
