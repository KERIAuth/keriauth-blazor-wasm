namespace Extension.Models;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Format hint controlling how a field value renders.
/// Values come from view-spec overrides (camelCase customs like
/// <c>"identicon"</c>, <c>"dateTimeAsUtc"</c>) or from a credential's JSON Schema
/// (kebab-case standards like <c>"date-time"</c>, <c>"idn-email"</c>).
/// </summary>
[JsonConverter(typeof(CredentialFieldFormatJsonConverter))]
public enum CredentialFieldFormat {
    // Custom (view-spec only)
    DateAsLocal,
    DateTimeAsLocal,
    DateAsUtc,
    DateTimeAsUtc,
    Identicon,
    Lei,

    // JSON Schema 2020-12 standard formats
    Date,
    Time,
    DateTime,
    Duration,
    Email,
    IdnEmail,
    Hostname,
    IdnHostname,
    Ipv4,
    Ipv6,
    Uri,
    UriReference,
    Iri,
    IriReference,
    Uuid,
    UriTemplate,
    JsonPointer,
    RelativeJsonPointer,
    Regex,
}

/// <summary>
/// Bidirectional string ↔ <see cref="CredentialFieldFormat"/> mapping. Used by both
/// the JSON deserializer (view-spec parsing) and the runtime schema-fallback path.
/// </summary>
public static class CredentialFieldFormatNames {
    private static readonly Dictionary<string, CredentialFieldFormat> s_byName =
        new(StringComparer.OrdinalIgnoreCase) {
            // Customs
            ["dateAsLocal"] = CredentialFieldFormat.DateAsLocal,
            ["dateTimeAsLocal"] = CredentialFieldFormat.DateTimeAsLocal,
            ["dateAsUtc"] = CredentialFieldFormat.DateAsUtc,
            ["dateTimeAsUtc"] = CredentialFieldFormat.DateTimeAsUtc,
            ["identicon"] = CredentialFieldFormat.Identicon,
            ["lei"] = CredentialFieldFormat.Lei,
            ["ISO 17442"] = CredentialFieldFormat.Lei,  // schema-declared format on LEI fields
            // JSON Schema 2020-12
            ["date"] = CredentialFieldFormat.Date,
            ["time"] = CredentialFieldFormat.Time,
            ["date-time"] = CredentialFieldFormat.DateTime,
            ["duration"] = CredentialFieldFormat.Duration,
            ["email"] = CredentialFieldFormat.Email,
            ["idn-email"] = CredentialFieldFormat.IdnEmail,
            ["hostname"] = CredentialFieldFormat.Hostname,
            ["idn-hostname"] = CredentialFieldFormat.IdnHostname,
            ["ipv4"] = CredentialFieldFormat.Ipv4,
            ["ipv6"] = CredentialFieldFormat.Ipv6,
            ["uri"] = CredentialFieldFormat.Uri,
            ["uri-reference"] = CredentialFieldFormat.UriReference,
            ["iri"] = CredentialFieldFormat.Iri,
            ["iri-reference"] = CredentialFieldFormat.IriReference,
            ["uuid"] = CredentialFieldFormat.Uuid,
            ["uri-template"] = CredentialFieldFormat.UriTemplate,
            ["json-pointer"] = CredentialFieldFormat.JsonPointer,
            ["relative-json-pointer"] = CredentialFieldFormat.RelativeJsonPointer,
            ["regex"] = CredentialFieldFormat.Regex,
        };

    private static readonly Dictionary<CredentialFieldFormat, string> s_toName =
        s_byName.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public static bool TryParse(string? name, out CredentialFieldFormat format) {
        if (name is not null && s_byName.TryGetValue(name, out format)) {
            return true;
        }
        format = default;
        return false;
    }

    /// <summary>Schema-fallback parser: returns null when <paramref name="name"/> is null or unknown.</summary>
    public static CredentialFieldFormat? ParseSchemaFormat(string? name) =>
        TryParse(name, out var f) ? f : null;

    public static string ToName(CredentialFieldFormat format) => s_toName[format];
}

internal sealed class CredentialFieldFormatJsonConverter : JsonConverter<CredentialFieldFormat> {
    public override CredentialFieldFormat Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var raw = reader.GetString();
        if (CredentialFieldFormatNames.TryParse(raw, out var format)) {
            return format;
        }
        throw new JsonException($"Unknown {nameof(CredentialFieldFormat)} value: '{raw}'");
    }

    public override void Write(Utf8JsonWriter writer, CredentialFieldFormat value, JsonSerializerOptions options) {
        writer.WriteStringValue(CredentialFieldFormatNames.ToName(value));
    }
}

/// <summary>
/// Specifies display properties for one field within a credential view.
/// </summary>
public record CredentialFieldSpec(
    string Path,                            // Dot-separated path in ACDC (e.g., "a.LEI", "e.qvi.n")
    int MinDetailLevel,                     // Field shown when user's detail level >= this value (0 = always shown)
    string? Label = null,                   // Override label (null = use schema description or field key)
    CredentialFieldFormat? Format = null    // Override format (null = use schema-derived format)
);

/// <summary>
/// View specification for a credential schema. Loaded from credentialViewSpecs.json.
/// </summary>
public record CredentialViewSpec(
    string SchemaSaid,                  // Schema SAID this spec applies to
    string ShortName,                   // Abbreviated display name (e.g., "LE vLEI")
    List<CredentialFieldSpec> Fields    // Field display specs, ordered by display position.
                                        // To hide framing keys (v, d, ri, s) at low detail, give them
                                        // entries here with MinDetailLevel: 9 — they'll only render at
                                        // the most-detailed setting.
);

public enum CredentialDisplayType { Card, Tree }

/// <summary>
/// Detail level presets used throughout the UI. Integer values are the thresholds
/// the pipeline compares against <see cref="CredentialFieldSpec.MinDetailLevel"/>
/// in credentialViewSpecs.json (that file stores raw ints so non-preset values
/// remain expressible).
/// </summary>
public enum CredentialDetailLevel {
    Basic = 0,
    WithOptionalSections = 1,
    Detailed = 2,
    WithTechnicalDetails = 9,
}

/// <summary>
/// Runtime display options for a CredentialComponent.
/// </summary>
public record CredentialViewOptions(
    CredentialDisplayType DisplayType = CredentialDisplayType.Card,
    CredentialDetailLevel DetailLevel = CredentialDetailLevel.WithOptionalSections,
    bool IsPresentation = false,                  // When true: enables elision controls and disclosure presets
    bool IsAidPrefixDisplay = true,               // When true: AIDs shown via display component; false: raw string
    bool IsJsonShown = false,                     // When true: show raw JSON expansion panel
    List<string>? PreselectedPresentationPaths = null  // Initial oneOf paths pre-selected for disclosure
);
