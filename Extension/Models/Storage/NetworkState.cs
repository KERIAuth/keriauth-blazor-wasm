namespace Extension.Models.Storage;

using System.Text.Json.Serialization;

/// <summary>
/// Network connectivity state stored in chrome.storage.session.
/// Written by BackgroundWorker based on navigator.onLine and signify operation results.
/// Read reactively by AppCache in all App instances.
///
/// Storage key: "NetworkState"
/// Storage area: Session
/// Lifetime: Cleared on browser close; re-established on service worker wake.
/// </summary>
public record NetworkState : IStorageModel {
    /// <summary>
    /// Whether the browser reports network connectivity (navigator.onLine).
    /// True when any network interface is up (including loopback).
    /// </summary>
    [JsonPropertyName("IsOnline")]
    public bool IsOnline { get; init; } = true;

    /// <summary>
    /// Whether the KERIA agent endpoint is reachable.
    /// Set to false when signify operations fail with network_error,
    /// set to true when any signify operation succeeds.
    /// </summary>
    [JsonPropertyName("IsKeriaReachable")]
    public bool IsKeriaReachable { get; init; } = true;
}
