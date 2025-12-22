using FluentResults;
using Extension.Models;

namespace Extension.Services;

public interface IWebauthnService {
    /// <summary>
    /// Creates a new WebAuthn passkey, verifies PRF support, encrypts the passcode,
    /// and stores the passkey in storage.
    /// </summary>
    /// <param name="residentKey">"required" | "preferred" | "discouraged"</param>
    /// <param name="authenticatorAttachment">"platform" | "cross-platform" or empty</param>
    /// <param name="userVerification">"required" | "preferred" | "discouraged"</param>
    /// <param name="attestationConveyancePreference">"none" | "indirect" | "direct" | "enterprise"</param>
    /// <param name="hints">Authenticator hints for browser UI</param>
    /// <returns>Name of the newly created passkey, or error</returns>
    Task<Result<string>> RegisterAttestStoreAuthenticatorAsync(
        string residentKey,
        string? authenticatorAttachment,
        string userVerification,
        string attestationConveyancePreference,
        List<string> hints);

    /// <summary>
    /// Authenticates with a stored passkey and returns the decrypted passcode.
    /// </summary>
    /// <returns>Decrypted passcode, or error if authentication fails</returns>
    Task<Result<string>> AuthenticateAndDecryptPasscodeAsync();

    /// <summary>
    /// Gets all stored passkeys from storage.
    /// Filters out any passkeys with incompatible schema versions.
    /// </summary>
    Task<Result<StoredPasskeys>> GetStoredPasskeysAsync();

    /// <summary>
    /// Removes a passkey by credential ID.
    /// </summary>
    /// <param name="credentialBase64">Base64URL-encoded credential ID to remove</param>
    Task<Result> RemovePasskeyAsync(string credentialBase64);

    /// <summary>
    /// Tests a specific passkey by credential ID.
    /// Verifies the passkey can successfully authenticate and decrypt the passcode.
    /// Only presents the specified credential to the browser.
    /// </summary>
    /// <param name="credentialBase64">Base64URL-encoded credential ID to test</param>
    /// <returns>Success if the passkey works, or error with details</returns>
    Task<Result> TestPasskeyAsync(string credentialBase64);

    /// <summary>
    /// Tests all stored passkeys by presenting them to the browser.
    /// Uses stored transports for each passkey (same behavior as Unlock with Passkey).
    /// </summary>
    /// <returns>Success with the name of the passkey that was used, or error with details</returns>
    Task<Result<string>> TestAllPasskeysAsync();
}
