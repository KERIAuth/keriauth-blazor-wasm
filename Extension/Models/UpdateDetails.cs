using System.Text.Json.Serialization;

namespace Extension.Models {
    /// <summary>
    /// Extension update details stored in local storage.
    /// Records information about extension installation and updates.
    ///
    /// Storage key: "UpdateDetails"
    /// Storage area: Local
    /// Lifetime: Persists until extension is uninstalled or storage is cleared
    /// </summary>
    public record UpdateDetails {
        /// <summary>
        /// The reason for the installation/update event.
        /// Values: "install", "update", "chrome_update", "shared_module_update"
        /// </summary>
        [JsonPropertyName("reason")]
        public required string Reason { get; init; }

        /// <summary>
        /// The previous version of the extension (for updates).
        /// Empty or "unknown" for fresh installations.
        /// </summary>
        [JsonPropertyName("previousVersion")]
        public required string PreviousVersion { get; init; }

        /// <summary>
        /// The current version of the extension.
        /// </summary>
        [JsonPropertyName("currentVersion")]
        public required string CurrentVersion { get; init; }

        /// <summary>
        /// ISO 8601 timestamp of when the update occurred.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public required string Timestamp { get; init; }
    }
}
