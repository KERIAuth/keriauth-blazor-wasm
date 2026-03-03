using System.Security.Cryptography;
using System.Text;
using Microsoft.JSInterop;

namespace Extension.Services.Crypto;

/// <summary>
/// Implementation of ICryptoService using native .NET crypto for SHA-256
/// and browser SubtleCrypto (via IJSRuntime) for AES-GCM.
/// </summary>
public class SubtleCryptoService(IJSRuntime jsRuntime, ILogger<SubtleCryptoService> logger) : ICryptoService {
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private readonly ILogger<SubtleCryptoService> _logger = logger;
    private IJSObjectReference? _cryptoModule;

    // TODO P3: Move this fixed string to a constant or configuration if it needs to be reused or changed in the future with rebranding.
    private static readonly byte[] labelBytes = [0x4B, 0x45, 0x52, 0x49, 0x20, 0x41, 0x75, 0x74, 0x68];

    /// <inheritdoc />
    public byte[] Sha256(byte[] data) {
        return SHA256.HashData(data);
    }

    /// <inheritdoc />
    // TODO P2: Consider using a more robust key derivation function (e.g., HKDF) instead of a simple hash-based approach.
    public byte[] DeriveKeyFromPrf(string profileId, byte[] prfOutput) {
        var profileIdBytes = Encoding.UTF8.GetBytes(profileId);
        // Concatenate: profileId || prfOutput || labelBytes
        var combined = new byte[profileIdBytes.Length + prfOutput.Length + labelBytes.Length];
        Buffer.BlockCopy(profileIdBytes, 0, combined, 0, profileIdBytes.Length);
        Buffer.BlockCopy(prfOutput, 0, combined, profileIdBytes.Length, prfOutput.Length);
        Buffer.BlockCopy(labelBytes, 0, combined, profileIdBytes.Length + prfOutput.Length, labelBytes.Length);
        return Sha256(combined);
    }

    /// <inheritdoc />
    public async Task<byte[]> AesGcmEncryptAsync(byte[] key, byte[] plaintext, byte[] nonce) {
        if (key.Length != 32) {
            throw new ArgumentException("AES-256 key must be 32 bytes", nameof(key));
        }
        if (nonce.Length != 12) {
            throw new ArgumentException("AES-GCM nonce must be 12 bytes", nameof(nonce));
        }

        await EnsureCryptoModuleLoadedAsync();

        var keyBase64 = Convert.ToBase64String(key);
        var plaintextBase64 = Convert.ToBase64String(plaintext);
        var nonceBase64 = Convert.ToBase64String(nonce);

        var ciphertextBase64 = await _cryptoModule!.InvokeAsync<string>(
            "aesGcmEncrypt",
            keyBase64,
            plaintextBase64,
            nonceBase64);

        return Convert.FromBase64String(ciphertextBase64);
    }

    /// <inheritdoc />
    public async Task<byte[]> AesGcmDecryptAsync(byte[] key, byte[] ciphertext, byte[] nonce) {
        if (key.Length != 32) {
            throw new ArgumentException("AES-256 key must be 32 bytes", nameof(key));
        }
        if (nonce.Length != 12) {
            throw new ArgumentException("AES-GCM nonce must be 12 bytes", nameof(nonce));
        }

        await EnsureCryptoModuleLoadedAsync();

        var keyBase64 = Convert.ToBase64String(key);
        var ciphertextBase64 = Convert.ToBase64String(ciphertext);
        var nonceBase64 = Convert.ToBase64String(nonce);

        var plaintextBase64 = await _cryptoModule!.InvokeAsync<string>(
            "aesGcmDecrypt",
            keyBase64,
            ciphertextBase64,
            nonceBase64);

        return Convert.FromBase64String(plaintextBase64);
    }

    /// <inheritdoc />
    public byte[] GetRandomBytes(int length) {
        return RandomNumberGenerator.GetBytes(length);
    }

    private async Task EnsureCryptoModuleLoadedAsync() {
        if (_cryptoModule is null) {
            _logger.LogDebug(nameof(EnsureCryptoModuleLoadedAsync) + ": Loading aesGcmCrypto.js module");
            _cryptoModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./scripts/es6/aesGcmCrypto.js");
        }
    }
}
