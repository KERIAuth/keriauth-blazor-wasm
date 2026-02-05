namespace Extension.Helper;

using Extension.Models;

/// <summary>
/// Helper class for computing passkey availability metrics.
/// Provides a consistent way to count and label passkeys matching a KERIA connection.
/// </summary>
public static class PasskeyHelper {
    /// <summary>
    /// Result type for GetPasskeysAvailable containing count and display label.
    /// </summary>
    public readonly record struct PasskeyAvailability(int Count, string Label) {
        /// <summary>
        /// True if at least one passkey is available for the given KERIA connection.
        /// </summary>
        public bool HasPasskeys => Count > 0;
    }

    /// <summary>
    /// Computes the number of passkeys available for a specific KERIA connection and generates
    /// a human-readable label.
    /// </summary>
    /// <param name="storedPasskeys">Collection of stored passkeys to filter.</param>
    /// <param name="keriaConnectionDigest">The KERIA connection digest to match against.</param>
    /// <returns>A tuple containing the count and label for matching passkeys.</returns>
    public static PasskeyAvailability GetPasskeysAvailable(
        StoredPasskeys storedPasskeys,
        string? keriaConnectionDigest
    ) {
        if (storedPasskeys is null || string.IsNullOrEmpty(keriaConnectionDigest)) {
            return new PasskeyAvailability(0, "No passkeys registered");
        }

        var count = storedPasskeys.Passkeys
            .Count(p => p.KeriaConnectionDigest == keriaConnectionDigest);

        var label = count switch {
            0 => "No passkeys registered",
            1 => "1 passkey registered",
            _ => $"{count} passkeys registered"
        };

        return new PasskeyAvailability(count, label);
    }

    /// <summary>
    /// Checks if a specific passkey is consistent with the given KERIA connection digest.
    /// </summary>
    /// <param name="passkey">The passkey to check.</param>
    /// <param name="keriaConnectionDigest">The KERIA connection digest to match against.</param>
    /// <returns>True if the passkey belongs to the specified KERIA connection.</returns>
    public static bool IsConsistentWithKeriaConnection(
        StoredPasskey passkey,
        string? keriaConnectionDigest
    ) {
        return !string.IsNullOrEmpty(keriaConnectionDigest) &&
               passkey.KeriaConnectionDigest == keriaConnectionDigest;
    }
}
