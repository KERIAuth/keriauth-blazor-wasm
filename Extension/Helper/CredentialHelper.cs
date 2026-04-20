using System.Collections.Concurrent;
using System.Text.Json;

namespace Extension.Helper;

/// <summary>
/// Helper methods for credential display and identification.
/// </summary>
public static class CredentialHelper {
    private static readonly JsonSerializerOptions DeserializeOptions = new() {
        PropertyNameCaseInsensitive = true,
        Converters = { new DictionaryConverter() }
    };

    /// <summary>
    /// Splits a raw JSON array of credentials into a dictionary keyed by sad.d (SAID).
    /// Uses JsonDocument to iterate elements and GetRawText() to preserve field ordering.
    /// </summary>
    public static Dictionary<string, string> SplitCredentialsArrayToDict(string rawJsonArray) {
        var result = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(rawJsonArray);
        foreach (var element in doc.RootElement.EnumerateArray()) {
            if (element.TryGetProperty("sad", out var sad) && sad.TryGetProperty("d", out var d)) {
                var said = d.GetString();
                if (!string.IsNullOrEmpty(said)) {
                    result[said] = element.GetRawText();
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Deserializes a dictionary of credentials (keyed by sad.d, values are raw JSON) into List of RecursiveDictionary.
    /// </summary>
    public static List<RecursiveDictionary> DeserializeCredentialsDict(Dictionary<string, string> credentials) {
        if (credentials is null || credentials.Count == 0) return [];
        return credentials.Values
            .Select(rawJson => {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(rawJson, DeserializeOptions);
                return dict is not null ? RecursiveDictionary.FromObjectDictionary(dict) : null;
            })
            .Where(rd => rd is not null)
            .ToList()!;
    }

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
        SediCredential2,
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
        public const string Sedi2 = "EHiLGNXjNR31E8hQR1Vs9OSWrG_CSpOOkVW76ZvUkaxq";
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
        SchemaSaids.Sedi2 => CredentialType.SediCredential2,
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
        SchemaSaids.Sedi2 => isDarkTheme ? "hsl(41 71% 29% / 1.00)" : "hsl(41 71% 80% / 1.00)",
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
        CredentialType.SediCredential2 => [
            ("fullLegalName.val", "Full legal name"),
            ("birthDate.val", "Birth date"),
            ("residenceAddress.val", "Residence address"),
            ("lawfulPresenceVerified.val", "Lawful presence verified"),
            ("proofingMethod.val", "Proofing method"),
            ("proofingLevel.val", "Proofing level"),
            ("portrait.val", "Portrait")
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

    /// <summary>
    /// Creates an elided copy of an ACDC by replacing undisclosed sections with their SAID strings.
    /// For each section key in elisionMap where value is false, the section dictionary is replaced
    /// with its "d" field value (the section's SAID).
    /// The top-level "d" is set to "" as a placeholder for signify-ts Saider.saidify to recompute.
    /// </summary>
    public static RecursiveDictionary ElideAcdc(RecursiveDictionary fullAcdc, Dictionary<string, bool> elisionMap) {
        var elided = new RecursiveDictionary();

        // Copy all entries from the original ACDC
        foreach (var kvp in fullAcdc) {
            elided[kvp.Key] = kvp.Value;
        }

        // Replace undisclosed sections with their SAID strings
        foreach (var (sectionKey, disclose) in elisionMap) {
            if (disclose) continue;
            if (!elided.TryGetValue(sectionKey, out var sectionValue)) continue;
            if (sectionValue.Type != RecursiveValueType.Dictionary) continue;
            if (sectionValue.Dictionary is null) continue;
            if (!sectionValue.Dictionary.TryGetValue("d", out var dValue)) continue;
            if (dValue.StringValue is null) continue;

            elided[sectionKey] = new RecursiveValue { StringValue = dValue.StringValue };
        }

        // Set top-level "d" to placeholder — signify-ts will recompute via Saider.saidify
        elided["d"] = new RecursiveValue { StringValue = "" };

        return elided;
    }
}
