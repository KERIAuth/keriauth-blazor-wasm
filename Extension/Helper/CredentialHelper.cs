using System.Collections.Concurrent;
using System.Text.Json;

namespace Extension.Helper;

/// <summary>
/// Corresponds to ACDC top-level keys that are schema-independent boilerplate.
/// Used to hide fields from display (cosmetic only).
/// Underscore prefix preserves the original key casing for readability.
/// </summary>
public enum SchemaIndependentDetail { _v, _d, _i, _ri, _s, _e, _r }

/// <summary>
/// Helper methods for credential display and identification.
/// </summary>
public static class CredentialHelper {
    private static readonly JsonSerializerOptions DeserializeOptions = new() {
        PropertyNameCaseInsensitive = true,
        Converters = { new DictionaryConverter() }
    };

    /// <summary>
    /// Deserializes raw JSON from signify-ts getCredentialsList() into List of RecursiveDictionary.
    /// Uses DictionaryConverter to preserve field ordering for CESR/SAID integrity.
    /// </summary>
    public static List<RecursiveDictionary> DeserializeCredentialsRawJson(string rawJson) {
        var credentialsDict = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(rawJson, DeserializeOptions);
        if (credentialsDict is null) {
            return [];
        }
        return credentialsDict.Select(RecursiveDictionary.FromObjectDictionary).ToList();
    }

    /// <summary>
    /// Known vLEI credential types mapped by their schema SAID.
    /// </summary>
    public enum CredentialType {
        OorCredential,
        EcrAuthCredential,
        EcrCredential,
        VleiCredential,
        OorAuthCredential,
        QviCredential,
        IxbrlAttestation,
        SediCredential,
        DataAttestation,
        DataAttestationCredential,
        Unknown
    }

    /// <summary>
    /// Schema SAIDs for known vLEI credential types.
    /// See Extension/Schemas/ for local copies of these schemas.
    /// </summary>
    public static class SchemaSaids {
        public const string Oor = "EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy";
        public const string EcrAuth = "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g";
        public const string Ecr = "EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw";
        public const string Vlei = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY";
        public const string OorAuth = "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E";
        public const string Qvi = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao";
        public const string Ixbrl = "EMhvwOlyEJ9kN4PrwCpr9Jsv7TxPhiYveZ0oP3lJzdEi";
        public const string Sedi = "EKEIy4dKkg1ygomPyDNJH4AiI3khx4ADy2s3hWBbsj2_";
        // public const string DataAttest = "EJxFPpyDRV-W6O2Vtjdy2K90ltWmQK8l1jePw5YOo_Ft";
        // public const string DataAttestCred = "ENDcMNUZjag27T_GTxiCmB2kYstg_kqipqz39906E_FD";
    }

    /// <summary>
    /// Gets the credential type from a schema SAID.
    /// </summary>
    public static CredentialType GetCredentialType(string? schemaSaid) => schemaSaid switch {
        SchemaSaids.Oor => CredentialType.OorCredential,
        SchemaSaids.EcrAuth => CredentialType.EcrAuthCredential,
        SchemaSaids.Ecr => CredentialType.EcrCredential,
        SchemaSaids.Vlei => CredentialType.VleiCredential,
        SchemaSaids.OorAuth => CredentialType.OorAuthCredential,
        SchemaSaids.Qvi => CredentialType.QviCredential,
        SchemaSaids.Ixbrl => CredentialType.IxbrlAttestation,
        SchemaSaids.Sedi => CredentialType.SediCredential,
        // SchemaSaids.DataAttest => CredentialType.DataAttestation,
        // SchemaSaids.DataAttestCred => CredentialType.DataAttestationCredential,
        _ => CredentialType.Unknown
    };

    /// <summary>
    /// Gets the background color for a credential based on its schema SAID.
    /// Colors have transparency to accommodate light/dark modes.
    /// </summary>
    public static string GetBackgroundColor(string? schemaSaid, bool isDarkTheme) => schemaSaid switch {
        SchemaSaids.Oor => isDarkTheme ? "hsl(210 30% 41% / 0.3)" : "hsl(210 30% 82% / 0.3)",
        SchemaSaids.EcrAuth => isDarkTheme ? "hsl(150 28% 40% / 0.75)" : "hsl(150 28% 80% / 0.75)",
        SchemaSaids.Ecr => isDarkTheme ? "hsl(35 35% 41% / 0.35)" : "hsl(35 35% 82% / 0.35)",
        SchemaSaids.Vlei => isDarkTheme ? "hsl(270 25% 41% / 0.75)" : "hsl(270 25% 82% / 0.75)",
        SchemaSaids.OorAuth => isDarkTheme ? "hsl(10 30% 40% / 0.75)" : "hsl(10 30% 80% / 0.75)",
        SchemaSaids.Qvi => isDarkTheme ? "hsl(195 30% 40% / 0.75)" : "hsl(195 30% 80% / 0.75)",
        SchemaSaids.Ixbrl => isDarkTheme ? "hsl(90 28% 41% / 0.75)" : "hsl(90 28% 82% / 0.75)",
        SchemaSaids.Sedi => isDarkTheme ? "hsl(41 71% 29% / 1.00)" : "hsl(41 71% 80% / 1.00)",
        // SchemaSaids.DataAttest => isDarkTheme ? "hsl(60 25% 40% / 0.75)" : "hsl(60 25% 82% / 0.75)",
        // SchemaSaids.DataAttestCred => isDarkTheme ? "hsl(45 28% 40% / 0.75)" : "hsl(45 28% 82% / 0.75)",
        _ => isDarkTheme ? "hsl(0 0% 43% / 0.75)" : "hsl(0 0% 85% / 0.75)"
    };

    /// <summary>
    /// Gets the background color for a credential from a RecursiveDictionary.
    /// Extracts the schema SAID from the "sad.s" path.
    /// </summary>
    public static string GetBackgroundColor(RecursiveDictionary? credential, bool isDarkTheme) {
        var schemaSaid = credential?.GetValueByPath("sad.s")?.Value?.ToString();
        return GetBackgroundColor(schemaSaid, isDarkTheme);
    }

    /// <summary>
    /// Represents a schema-driven field with label, value, and optional format hint.
    /// </summary>
    public record SchemaFieldDisplay(string? Label, string? Value, string? Format);

    /// <summary>
    /// Cached schema metadata (labels and formats) keyed by schema SAID, then by field name.
    /// Values differ per credential instance, so only labels and formats are cached.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (string? Label, string? Format)>>
        SchemaMetadataCache = new();

    /// <summary>
    /// Extracts a schema-driven field (label from schema, value from credential, format from schema).
    /// Assumes the common nesting pattern: schema properties in schemaProps at "properties.{fieldName}.*"
    /// and credential values at "sad.a.{fieldName}".
    /// Caches schema-level metadata (label, format) by schema SAID for subsequent lookups.
    /// </summary>
    public static SchemaFieldDisplay GetSchemaField(
        RecursiveDictionary credential,
        RecursiveDictionary? schemaProps,
        string fieldName) {
        var schemaSaid = credential.GetValueByPath("sad.s")?.Value?.ToString();
        var value = credential.GetValueByPath($"sad.a.{fieldName}")?.Value?.ToString();

        if (schemaSaid is not null) {
            var fieldCache = SchemaMetadataCache.GetOrAdd(schemaSaid, _ => new());
            var (label, format) = fieldCache.GetOrAdd(fieldName, _ => {
                var l = schemaProps?.GetValueByPath($"properties.{fieldName}.description")?.Value?.ToString();
                var f = schemaProps?.GetValueByPath($"properties.{fieldName}.format")?.Value?.ToString();
                return (l, f);
            });
            return new SchemaFieldDisplay(label, value, format);
        }

        var labelUncached = schemaProps?.GetValueByPath($"properties.{fieldName}.description")?.Value?.ToString();
        var formatUncached = schemaProps?.GetValueByPath($"properties.{fieldName}.format")?.Value?.ToString();
        return new SchemaFieldDisplay(labelUncached, value, formatUncached);
    }

    /// <summary>
    /// Formats a field value based on its schema format hint.
    /// </summary>
    public static string FormatFieldValue(string? value, string? format) =>
        (value, format) switch {
            (null, _) => "",
            (_, "date") => value.Length >= 10 ? value[..10] : value,
            (_, "date-time") => value.Length >= 10 ? value[..10] : value,
            _ => value
        };

    /// <summary>
    /// Returns the credential-specific display fields for a given credential type.
    /// Each field has a name (matching the schema's attribute property key) and a default label
    /// used when the schema description is unavailable.
    /// </summary>
    public static (string FieldName, string DefaultLabel)[] GetDisplayFields(CredentialType credType) => credType switch {
        CredentialType.SediCredential => [
            ("firstName", "First Name"),
            ("lastName", "Last Name"),
            ("dateOfBirth", "Date of Birth"),
            ("address", "Address"),
            ("driversLicense", "Driver's License")
        ],
        CredentialType.QviCredential => [
            ("LEI", "LEI"),
            ("gracePeriod", "Grace Period")
        ],
        CredentialType.VleiCredential => [
            ("LEI", "LEI")
        ],
        CredentialType.OorCredential => [
            ("LEI", "LEI"),
            ("personLegalName", "Legal Name"),
            ("officialRole", "Official Role")
        ],
        CredentialType.OorAuthCredential => [
            ("AID", "Recipient AID"),
            ("LEI", "LEI"),
            ("personLegalName", "Legal Name"),
            ("officialRole", "Official Role")
        ],
        CredentialType.EcrCredential => [
            ("LEI", "LEI"),
            ("personLegalName", "Legal Name"),
            ("engagementContextRole", "Engagement Context Role")
        ],
        CredentialType.EcrAuthCredential => [
            ("AID", "Recipient AID"),
            ("LEI", "LEI"),
            ("personLegalName", "Legal Name"),
            ("engagementContextRole", "Engagement Context Role")
        ],
        CredentialType.DataAttestation => [
            ("digest", "Digest")
        ],
        CredentialType.DataAttestationCredential => [
            ("digest", "Digest"),
            ("digestAlgo", "Digest Algorithm")
        ],
        _ => []
    };

    /// <summary>
    /// Filters a list of credential RecursiveDictionaries by path/value pairs.
    /// Returns credentials where any filter matches (OR logic).
    /// </summary>
    public static List<RecursiveDictionary> FilterCredentials(List<RecursiveDictionary> credentialDictList, List<(string filterPath, string match)> filters) {
        if (filters.Count == 0) {
            throw new ArgumentException("FilterCredentials must have at least one filter");
        }
        List<RecursiveDictionary> filteredCredentials = new();
        foreach (var credDict in credentialDictList) {
            foreach (var filter in filters) {
                var value = credDict.GetValueByPath(filter.filterPath)?.Value as string;
                if (value != null && value == filter.match) {
                    filteredCredentials.Add(credDict);
                    break;
                }
            }
        }
        return filteredCredentials;
    }
}
