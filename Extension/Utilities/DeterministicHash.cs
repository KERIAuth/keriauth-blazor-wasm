namespace Extension.Utilities;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Provides deterministic hashing for strings across different Blazor WASM runtime instances.
///
/// Why this exists:
/// - String.GetHashCode() is non-deterministic across process executions in .NET Core/.NET 5+/.NET 9
/// - This is by design for security (prevents DoS attacks via hash flooding)
/// - Browser extension has TWO separate Blazor WASM runtimes:
///   1. BackgroundWorker runtime (service worker context)
///   2. App runtime (UI context - popup, tabs, sidepanel)
/// - Passcode hash computed in App runtime != hash computed in BackgroundWorker runtime
/// - Need deterministic hash that works across both runtimes
///
/// Security note:
/// - This uses SHA256 for deterministic hashing
/// - Passcode should be adequately strong (not vulnerable to rainbow tables)
/// - Hash is stored in Local storage (not exposed to content script or web pages)
/// </summary>
public static class DeterministicHash {
    /// <summary>
    /// Computes a deterministic 32-bit hash of a string using SHA256.
    /// Same input always produces same output, regardless of runtime instance.
    /// </summary>
    /// <param name="input">String to hash</param>
    /// <returns>32-bit integer hash (from first 4 bytes of SHA256)</returns>
    public static int ComputeHash(string input) {
        if (string.IsNullOrEmpty(input)) {
            return 0;
        }

        // Compute SHA256 hash
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(bytes);

        // Take first 4 bytes and convert to int32
        // This is deterministic across all runtimes
        return BitConverter.ToInt32(hashBytes, 0);
    }
}
