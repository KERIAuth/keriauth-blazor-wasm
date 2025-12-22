using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Services;

/// <summary>
/// Metadata for an authenticator from FIDO convenience metadata.
/// </summary>
public record AuthenticatorMetadata {
    /// <summary>
    /// Localized friendly names for the authenticator.
    /// Key is locale code (e.g., "en-US"), value is the display name.
    /// </summary>
    [JsonPropertyName("friendlyNames")]
    public Dictionary<string, string>? FriendlyNames { get; init; }

    /// <summary>
    /// Data URL for the authenticator's icon (e.g., "data:image/png;base64,...").
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; init; }

    /// <summary>
    /// Data URL for the authenticator's dark mode icon.
    /// </summary>
    [JsonPropertyName("iconDark")]
    public string? IconDark { get; init; }
}

/// <summary>
/// Service for looking up authenticator metadata from FIDO convenience metadata.
/// </summary>
public interface IFidoMetadataService {
    /// <summary>
    /// Gets metadata for an authenticator by AAGUID.
    /// </summary>
    /// <param name="aaguid">AAGUID in UUID format (e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96")</param>
    /// <returns>Metadata if found, null otherwise</returns>
    AuthenticatorMetadata? GetMetadata(string aaguid);

    /// <summary>
    /// Gets the friendly name for an authenticator.
    /// </summary>
    /// <param name="aaguid">AAGUID in UUID format</param>
    /// <param name="locale">Preferred locale (defaults to "en-US")</param>
    /// <returns>Friendly name if found, null otherwise</returns>
    string? GetFriendlyName(string aaguid, string locale = "en-US");

    /// <summary>
    /// Generates a descriptive name for an authenticator based on AAGUID and transports.
    /// </summary>
    /// <param name="aaguid">AAGUID in UUID format</param>
    /// <param name="transports">Transport types (e.g., ["internal"], ["usb", "nfc"])</param>
    /// <returns>Descriptive name like "Google Password Manager (Platform Authenticator)"</returns>
    string GenerateDescriptiveName(string aaguid, string[] transports);
}

/// <summary>
/// Implementation of FIDO metadata service that loads from embedded JSON file.
/// </summary>
public class FidoMetadataService : IFidoMetadataService {
    private readonly Dictionary<string, AuthenticatorMetadata> _metadata;
    private readonly ILogger<FidoMetadataService> _logger;

    public FidoMetadataService(ILogger<FidoMetadataService> logger) {
        _logger = logger;
        _metadata = LoadMetadata();
    }

    private Dictionary<string, AuthenticatorMetadata> LoadMetadata() {
        try {
            // Load from embedded resource
            var assembly = typeof(FidoMetadataService).Assembly;
            var resourceName = "Extension.Data.fido-convenience-metadata.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) {
                _logger.LogWarning("FIDO convenience metadata resource not found: {ResourceName}", resourceName);
                return new Dictionary<string, AuthenticatorMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var result = JsonSerializer.Deserialize<Dictionary<string, AuthenticatorMetadata>>(json);
            if (result is null) {
                _logger.LogWarning("Failed to deserialize FIDO convenience metadata");
                return new Dictionary<string, AuthenticatorMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            _logger.LogInformation("Loaded {Count} authenticator metadata entries", result.Count);
            return new Dictionary<string, AuthenticatorMetadata>(result, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error loading FIDO convenience metadata");
            return new Dictionary<string, AuthenticatorMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public AuthenticatorMetadata? GetMetadata(string aaguid) {
        if (string.IsNullOrEmpty(aaguid)) {
            return null;
        }

        _metadata.TryGetValue(aaguid, out var metadata);
        return metadata;
    }

    public string? GetFriendlyName(string aaguid, string locale = "en-US") {
        var metadata = GetMetadata(aaguid);
        if (metadata?.FriendlyNames is null) {
            return null;
        }

        // Try exact locale match first
        if (metadata.FriendlyNames.TryGetValue(locale, out var name)) {
            return name;
        }

        // Try language-only fallback (e.g., "en" from "en-US")
        var languageCode = locale.Split('-')[0];
        foreach (var (key, value) in metadata.FriendlyNames) {
            if (key.StartsWith(languageCode, StringComparison.OrdinalIgnoreCase)) {
                return value;
            }
        }

        // Return first available name
        return metadata.FriendlyNames.Values.FirstOrDefault();
    }

    public string GenerateDescriptiveName(string aaguid, string[] transports) {
        var friendlyName = GetFriendlyName(aaguid);
        var transportDescription = GetTransportDescription(transports);

        if (friendlyName is not null && transportDescription is not null) {
            return $"{friendlyName} ({transportDescription})";
        }

        if (friendlyName is not null) {
            return friendlyName;
        }

        if (transportDescription is not null) {
            return transportDescription;
        }

        return "Authenticator";
    }

    private static string? GetTransportDescription(string[] transports) {
        if (transports.Length == 0) {
            return null;
        }

        // Check for specific transport patterns
        var hasInternal = transports.Contains("internal", StringComparer.OrdinalIgnoreCase);
        var hasUsb = transports.Contains("usb", StringComparer.OrdinalIgnoreCase);
        var hasNfc = transports.Contains("nfc", StringComparer.OrdinalIgnoreCase);
        var hasBle = transports.Contains("ble", StringComparer.OrdinalIgnoreCase);
        var hasHybrid = transports.Contains("hybrid", StringComparer.OrdinalIgnoreCase);

        // Platform authenticator (only internal)
        if (hasInternal && transports.Length == 1) {
            return "Platform Authenticator";
        }

        // USB security key (has USB, no internal)
        if (hasUsb && !hasInternal) {
            if (hasNfc) {
                return "USB/NFC Security Key";
            }
            return "USB Security Key";
        }

        // NFC only
        if (hasNfc && !hasUsb && !hasInternal) {
            return "NFC Security Key";
        }

        // Bluetooth
        if (hasBle && !hasUsb && !hasInternal && !hasNfc) {
            return "Bluetooth Security Key";
        }

        // Hybrid (phone as authenticator)
        if (hasHybrid && !hasUsb && !hasInternal && !hasNfc && !hasBle) {
            return "Cross-Device Authenticator";
        }

        // Mixed transports - describe as security key
        if (!hasInternal) {
            return "Security Key";
        }

        // Has internal with other transports
        return "Authenticator";
    }
}
