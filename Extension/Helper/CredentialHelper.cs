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
        _ => CredentialType.Unknown
    };

    /// <summary>
    /// Gets the background color for a credential based on its schema SAID.
    /// Colors have transparency to accommodate light/dark modes.
    /// </summary>
    public static string GetBackgroundColor(string? schemaSaid) => schemaSaid switch {
        SchemaSaids.Oor => "hsl(210 30% 82% / 0.3)",      // Soft steel blue – neutral, professional
        SchemaSaids.EcrAuth => "hsl(150 28% 80% / 0.75)", // Muted mint green – calm, positive
        SchemaSaids.Ecr => "hsl(35 35% 82% / 0.35)",      // Warm sand – friendly and readable
        SchemaSaids.Vlei => "hsl(270 25% 82% / 0.75)",    // Dusty lavender – subtle distinction
        SchemaSaids.OorAuth => "hsl(10 30% 80% / 0.75)",  // Soft clay / muted coral
        SchemaSaids.Qvi => "hsl(195 30% 80% / 0.75)",     // Desaturated teal – modern, balanced
        SchemaSaids.Ixbrl => "hsl(90 28% 82% / 0.75)",    // Pale olive – earthy, understated
        SchemaSaids.Sedi => "hsl(41 71% 57% / 1.00)",     // Orange
        _ => "hsl(0 0% 85% / 0.75)"                       // Neutral gray – safest baseline
    };

    /// <summary>
    /// Gets the background color for a credential from a RecursiveDictionary.
    /// Extracts the schema SAID from the "sad.s" path.
    /// </summary>
    public static string GetBackgroundColor(RecursiveDictionary? credential) {
        var schemaSaid = credential?.GetValueByPath("sad.s")?.Value?.ToString();
        return GetBackgroundColor(schemaSaid);
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
        string fieldName)
    {
        var schemaSaid = credential.GetValueByPath("sad.s")?.Value?.ToString();
        var value = credential.GetValueByPath($"sad.a.{fieldName}")?.Value?.ToString();

        if (schemaSaid is not null)
        {
            var fieldCache = SchemaMetadataCache.GetOrAdd(schemaSaid, _ => new());
            var (label, format) = fieldCache.GetOrAdd(fieldName, _ =>
            {
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
