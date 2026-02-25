using System.Text.Json.Serialization;

namespace Extension.Models.Messages.AppBw.Requests;

public record RequestGetOobiPayload(
    [property: JsonPropertyName("aidName")] string AidName
);

public record AppBwRequestGetOobiMessage : AppBwMessage<RequestGetOobiPayload>
{
    public AppBwRequestGetOobiMessage(string aidName)
        : base(AppBwMessageType.RequestGetOobi, 0, null, null, new RequestGetOobiPayload(aidName)) { }
}
