using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Helper;

/// <summary>
/// Centralized JsonSerializerOptions definitions for the extension.
/// Use these shared instances instead of creating new JsonSerializerOptions objects.
/// </summary>
/// <remarks>
/// <para><b>TODO P1: Inconsistencies found during consolidation (investigate later):</b></para>
/// <list type="number">
/// <item>
/// <term>AppPortService vs BwPortService naming mismatch</term>
/// <description>
/// AppPortService used CamelCase for port messages but NOT for credential payloads.
/// BwPortService used CamelCase for everything including credentials.
/// Now both use PortMessaging (with CamelCase) for consistency.
/// TODO: Verify credentials serialize correctly with CamelCase naming policy.
/// </description>
/// </item>
/// <item>
/// <term>CamelCaseOmitNull vs CamelCase</term>
/// <description>
/// NavigatorCredentialsBinding uses CamelCaseOmitNull (omits nulls).
/// Other JS interop uses CamelCase (preserves nulls).
/// These may need to be unified or the difference documented.
/// </description>
/// </item>
/// <item>
/// <term>DeepNested unused</term>
/// <description>
/// DeepNested (MaxDepth=128, no RecursiveDictionary) was defined but
/// all deep-nested usages now use PortMessaging (with RecursiveDictionary).
/// Consider removing DeepNested if not needed.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class JsonOptions {
    /// <summary>
    /// Default options for general deserialization.
    /// Case-insensitive property matching.
    /// </summary>
    public static readonly JsonSerializerOptions Default = new() {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for JavaScript interop with camelCase naming.
    /// Use when serializing/deserializing data to/from JavaScript.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCase = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Options for WebAuthn/NavigatorCredentials interop.
    /// CamelCase naming with null values omitted.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseOmitNull = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Options for deeply nested credential structures.
    /// MaxDepth=128 handles vLEI credentials with deep nesting (edges, rules, chains).
    /// </summary>
    public static readonly JsonSerializerOptions DeepNested = new() {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 128
    };

    /// <summary>
    /// Options for credential data that must preserve field ordering for CESR/SAID.
    /// Uses RecursiveDictionaryConverter to maintain insertion order.
    /// CRITICAL: Use this when handling ACDC credentials to preserve SAID integrity.
    /// </summary>
    public static readonly JsonSerializerOptions RecursiveDictionary = new() {
        Converters = { new RecursiveDictionaryConverter() }
    };

    /// <summary>
    /// Options for port messaging with deeply nested credential structures.
    /// Combines camelCase, case-insensitive, MaxDepth=128, and RecursiveDictionary.
    /// Use for BW↔CS and BW↔App message serialization involving credentials.
    /// </summary>
    public static readonly JsonSerializerOptions PortMessaging = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 128,
        Converters = { new RecursiveDictionaryConverter() }
    };

    /// <summary>
    /// Options for chrome.storage serialization.
    /// Preserves PascalCase from C# properties, includes fields.
    /// </summary>
    public static readonly JsonSerializerOptions Storage = new() {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        PropertyNamingPolicy = null // Preserve PascalCase from C# properties
    };

    /// <summary>
    /// Options for displaying JSON in UI (human-readable).
    /// WriteIndented with null values omitted.
    /// </summary>
    public static readonly JsonSerializerOptions Display = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Options for displaying JSON in UI with indentation only.
    /// Use when null values should be preserved in output.
    /// </summary>
    public static readonly JsonSerializerOptions DisplayIndented = new() {
        WriteIndented = true
    };
}
