using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Models.Messages.AppBw.Requests;

/// <summary>
/// Payload for App → BW saidify request. The <c>section</c> is a credential block/object
/// (typically sad.a, sad.e, or sad.r) whose <c>d</c> field should be computed via
/// signify-ts' Saider.saidify. Insertion order is preserved via RecursiveDictionaryConverter.
/// </summary>
public record RequestSaidifyPayload(
    [property: JsonPropertyName("section")] RecursiveDictionary Section
);

public record AppBwRequestSaidifyMessage : AppBwMessage<RequestSaidifyPayload>
{
    public AppBwRequestSaidifyMessage(RecursiveDictionary section)
        : base(AppBwMessageType.RequestSaidify, 0, null, null, new RequestSaidifyPayload(section)) { }
}
