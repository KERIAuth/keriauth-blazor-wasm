using System.Collections.Specialized;
using System.Text.Json.Serialization;
using Extension.Helper;

namespace Extension.Services.SignifyService.Models {
    public record ExchangeMessage(
        [property: JsonPropertyName("v")] string V,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("i")] string I,
        [property: JsonPropertyName("dt")] string Dt,
        [property: JsonPropertyName("rp")] string? Rp = null,
        [property: JsonPropertyName("p")] string? P = null,
        [property: JsonPropertyName("r")] string? R = null,
        [property: JsonPropertyName("q")] OrderedDictionary? Q = null,
        [property: JsonPropertyName("a")] OrderedDictionary? A = null,
        [property: JsonPropertyName("e")] OrderedDictionary? E = null
    );

    public record ExchangeArgs(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("route")] string Route,
        [property: JsonPropertyName("payload")] KeriDictionary Payload,
        [property: JsonPropertyName("embeds")] KeriDictionary? Embeds = null,
        [property: JsonPropertyName("recipients")] List<string>? Recipients = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record ForwardArgs(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("serder")] Serder Serder,
        [property: JsonPropertyName("attachments")] List<string>? Attachments = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record MultisigExnArgs(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("recipients")] List<string> Recipients,
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("payload")] KeriDictionary Payload,
        [property: JsonPropertyName("embeds")] KeriDictionary? Embeds = null,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record QueryArgs(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("query")] KeriDictionary Query,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record ReplyArgs(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("topic")] string Topic,
        [property: JsonPropertyName("reply")] KeriDictionary Reply,
        [property: JsonPropertyName("datetime")] string? Datetime = null
    );

    public record Exchange(
        [property: JsonPropertyName("said")] string Said,
        [property: JsonPropertyName("exn")] ExchangeMessage Exn,
        [property: JsonPropertyName("attachments")] List<string>? Attachments = null
    );

    /// <summary>
    /// Typed read model for inbound KERIA exchange messages.
    /// Extracts scalar fields from the raw RecursiveDictionary while preserving
    /// ordered RecursiveDictionary for CESR/SAID-sensitive fields (A, E, Q).
    /// </summary>
    public readonly record struct ExchangeView(
        string? D,
        string? I,
        string? Rp,
        string? Dt,
        string? R,
        string? P,
        RecursiveDictionary? A,
        RecursiveDictionary? E,
        RecursiveDictionary? Q,
        RecursiveDictionary RawExn
    ) {
        /// <summary>
        /// Creates an ExchangeView from a RecursiveDictionary wrapper.
        /// Handles the { exn: { ... } } wrapper pattern, with fallback to top-level.
        /// </summary>
        public static ExchangeView FromRecursiveDictionary(RecursiveDictionary wrapper) {
            RecursiveDictionary exn;
            if (wrapper.TryGetValue("exn", out var exnVal) && exnVal?.Dictionary is RecursiveDictionary exnDict) {
                exn = exnDict;
            }
            else {
                exn = wrapper;
            }

            return new ExchangeView(
                D: GetString(exn, "d"),
                I: GetString(exn, "i"),
                Rp: GetString(exn, "rp"),
                Dt: GetString(exn, "dt"),
                R: GetString(exn, "r"),
                P: GetString(exn, "p"),
                A: GetDict(exn, "a"),
                E: GetDict(exn, "e"),
                Q: GetDict(exn, "q"),
                RawExn: exn
            );
        }

        private static string? GetString(RecursiveDictionary dict, string key) =>
            dict.TryGetValue(key, out var val) ? val?.StringValue : null;

        private static RecursiveDictionary? GetDict(RecursiveDictionary dict, string key) =>
            dict.TryGetValue(key, out var val) ? val?.Dictionary : null;

        // Approach A: Infer flow type from the user's role relative to the notification.
        // Assumption: if the user is the TARGET of a notification, the route implies a specific flow direction.
        // For example, receiving a "grant" as target means someone is issuing TO the user (Issuance).
        // Receiving an "apply" as target means someone is requesting FROM the user (Presentation).
        // This heuristic may be wrong if the user is both issuer and holder, or in edge cases.
        public static IpexFlowType InferFlowFromRole(string route, string? targetPrefix, IEnumerable<string> userPrefixes) {
            if (targetPrefix is null) return IpexFlowType.Unknown;
            var userIsTarget = userPrefixes.Contains(targetPrefix);
            return route switch {
                "/exn/ipex/apply" => userIsTarget ? IpexFlowType.Presentation : IpexFlowType.Issuance,
                "/exn/ipex/offer" => userIsTarget ? IpexFlowType.Issuance : IpexFlowType.Presentation,
                "/exn/ipex/agree" => userIsTarget ? IpexFlowType.Presentation : IpexFlowType.Issuance,
                "/exn/ipex/grant" => userIsTarget ? IpexFlowType.Issuance : IpexFlowType.Presentation,
                "/exn/ipex/admit" => userIsTarget ? IpexFlowType.Presentation : IpexFlowType.Issuance,
                _ => IpexFlowType.Unknown
            };
        }

        // Approach B: Infer flow type by comparing the exchange sender with the ACDC issuer.
        // Only works when the exchange embeds an ACDC (typically grant messages).
        // If the sender IS the ACDC issuer, this is an issuance (issuer sending their own credential).
        // If the sender is NOT the ACDC issuer, this is a presentation (holder presenting someone else's credential).
        public static IpexFlowType InferFlowFromAcdc(string? senderPrefix, RecursiveDictionary? embeddedData) {
            if (senderPrefix is null || embeddedData is null) return IpexFlowType.Unknown;
            var acdcIssuer = embeddedData.GetValueByPath("acdc.i")?.Value?.ToString();
            if (acdcIssuer is null) return IpexFlowType.Unknown;
            return senderPrefix == acdcIssuer ? IpexFlowType.Issuance : IpexFlowType.Presentation;
        }
    }

    public enum IpexFlowType { Unknown, Issuance, Presentation }
}
