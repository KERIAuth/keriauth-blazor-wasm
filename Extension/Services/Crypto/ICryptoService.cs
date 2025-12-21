namespace Extension.Services.Crypto;

/// <summary>
/// Provides cryptographic operations for WebAuthn PRF-based key derivation and encryption.
/// Uses native .NET crypto for SHA-256 and Blazor.SubtleCrypto for AES-GCM.
/// </summary>
public interface ICryptoService {
    /// <summary>
    /// Computes SHA-256 hash of the input data.
    /// Uses native .NET System.Security.Cryptography (synchronous, no JS interop).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>32-byte SHA-256 hash.</returns>
    byte[] Sha256(byte[] data);

    /// <summary>
    /// Derives a 256-bit AES encryption key from PRF output using SHA-256.
    /// Formula: SHA256(profileId || prfOutput || "KERI Auth")
    /// </summary>
    /// <param name="profileId">The browser profile identifier (UUID string).</param>
    /// <param name="prfOutput">The PRF output from WebAuthn authenticator (32 bytes).</param>
    /// <returns>32-byte derived key suitable for AES-256-GCM.</returns>
    byte[] DeriveKeyFromPrf(string profileId, byte[] prfOutput);

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// Uses Blazor.SubtleCrypto (requires async JS interop).
    /// </summary>
    /// <param name="key">32-byte AES-256 key.</param>
    /// <param name="plaintext">Data to encrypt.</param>
    /// <param name="nonce">12-byte nonce/IV.</param>
    /// <returns>Ciphertext with authentication tag appended.</returns>
    Task<byte[]> AesGcmEncryptAsync(byte[] key, byte[] plaintext, byte[] nonce);

    /// <summary>
    /// Decrypts ciphertext using AES-256-GCM.
    /// Uses Blazor.SubtleCrypto (requires async JS interop).
    /// </summary>
    /// <param name="key">32-byte AES-256 key.</param>
    /// <param name="ciphertext">Encrypted data with authentication tag.</param>
    /// <param name="nonce">12-byte nonce/IV used during encryption.</param>
    /// <returns>Decrypted plaintext.</returns>
    Task<byte[]> AesGcmDecryptAsync(byte[] key, byte[] ciphertext, byte[] nonce);

    /// <summary>
    /// Generates cryptographically secure random bytes.
    /// Uses native .NET RandomNumberGenerator (synchronous, no JS interop).
    /// </summary>
    /// <param name="length">Number of random bytes to generate.</param>
    /// <returns>Random byte array of specified length.</returns>
    byte[] GetRandomBytes(int length);
}
