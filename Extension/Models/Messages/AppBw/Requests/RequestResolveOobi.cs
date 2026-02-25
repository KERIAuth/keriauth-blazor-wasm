using System.Text.Json.Serialization;

namespace Extension.Models.Messages.AppBw.Requests;

public record RequestResolveOobiPayload(
    [property: JsonPropertyName("oobiUrl")] string OobiUrl,
    [property: JsonPropertyName("alias")] string? Alias = null
);

public record AppBwRequestResolveOobiMessage : AppBwMessage<RequestResolveOobiPayload>
{
    public AppBwRequestResolveOobiMessage(string oobiUrl, string? alias = null)
        : base(AppBwMessageType.RequestResolveOobi, 0, null, null, new RequestResolveOobiPayload(oobiUrl, alias)) { }
}
