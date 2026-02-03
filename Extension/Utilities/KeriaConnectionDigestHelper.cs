namespace Extension.Utilities;

using System.Security.Cryptography;
using System.Text;
using Extension.Models;
using FluentResults;

/// <summary>
/// Helper class for computing KeriaConnectionDigest values.
/// The digest uniquely identifies a KERIA connection configuration.
/// </summary>
public static class KeriaConnectionDigestHelper {
    /// <summary>
    /// Computes the KeriaConnectionDigest as a hex-encoded SHA256 hash of
    /// ClientAidPrefix + AgentAidPrefix + PasscodeHash.
    /// This ensures a deterministic digest based on the KERIA connection configuration.
    /// </summary>
    /// <param name="config">The KERIA connection configuration.</param>
    /// <returns>A 64-character lowercase hex SHA256 hash, or an error if required fields are missing.</returns>
    public static Result<string> Compute(KeriaConnectConfig config) {
        if (string.IsNullOrWhiteSpace(config.ClientAidPrefix)) {
            return Result.Fail<string>("ClientAidPrefix is required to compute KeriaConnectionDigest");
        }
        if (string.IsNullOrWhiteSpace(config.AgentAidPrefix)) {
            return Result.Fail<string>("AgentAidPrefix is required to compute KeriaConnectionDigest");
        }
        if (config.PasscodeHash == 0) {
            return Result.Fail<string>("PasscodeHash is required to compute KeriaConnectionDigest");
        }

        var input = config.ClientAidPrefix + config.AgentAidPrefix +
                    config.PasscodeHash.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hexString = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Result.Ok(hexString);
    }
}
