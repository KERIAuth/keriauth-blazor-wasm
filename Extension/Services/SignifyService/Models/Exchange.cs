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
    }
}
