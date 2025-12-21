using FluentResults;
using Extension.Models;

namespace Extension.Services;

public interface IWebauthnService {
    /// <summary>
    /// Registers a new WebAuthn authenticator, verifies PRF support, encrypts the passcode,
    /// and stores the authenticator in storage.
    /// </summary>
    /// <param name="residentKey">"required" | "preferred" | "discouraged"</param>
    /// <param name="authenticatorAttachment">"platform" | "cross-platform" or empty</param>
    /// <param name="userVerification">"required" | "preferred" | "discouraged"</param>
    /// <param name="attestationConveyancePreference">"none" | "indirect" | "direct" | "enterprise"</param>
    /// <param name="hints">Authenticator hints for browser UI</param>
    /// <returns>Name of the newly registered authenticator, or error</returns>
    Task<Result<string>> RegisterAttestStoreAuthenticatorAsync(
        string residentKey,
        string authenticatorAttachment,
        string userVerification,
        string attestationConveyancePreference,
        List<string> hints);

    /// <summary>
    /// Authenticates with a registered WebAuthn authenticator and returns the decrypted passcode.
    /// </summary>
    /// <returns>Decrypted passcode, or error if authentication fails</returns>
    Task<Result<string>> AuthenticateAndDecryptPasscodeAsync();

    /// <summary>
    /// Gets all registered authenticators from storage.
    /// Filters out any authenticators with incompatible schema versions.
    /// </summary>
    Task<Result<RegisteredAuthenticators>> GetRegisteredAuthenticatorsAsync();

    /// <summary>
    /// Removes an authenticator by credential ID.
    /// </summary>
    /// <param name="credentialBase64">Base64URL-encoded credential ID to remove</param>
    Task<Result> RemoveAuthenticatorAsync(string credentialBase64);

    /// <summary>
    /// Tests a specific authenticator by credential ID.
    /// Verifies the authenticator can successfully authenticate and decrypt the passcode.
    /// Only presents the specified credential to the browser.
    /// </summary>
    /// <param name="credentialBase64">Base64URL-encoded credential ID to test</param>
    /// <returns>Success if the authenticator works, or error with details</returns>
    Task<Result> TestAuthenticatorAsync(string credentialBase64);

    /// <summary>
    /// Tests all registered authenticators by presenting them to the browser.
    /// Uses stored transports for each authenticator (same behavior as Unlock with Passkey).
    /// </summary>
    /// <returns>Success with the name of the authenticator that was used, or error with details</returns>
    Task<Result<string>> TestAllAuthenticatorsAsync();
}
