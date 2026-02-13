using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Helper;
using FluentResults;
using Microsoft.JSInterop;

namespace Extension.Services.JsBindings;

/// <summary>
/// Result of WebAuthn credential creation (registration).
/// </summary>
public record CredentialCreationResult {
    /// <summary>Base64URL-encoded credential ID</summary>
    [JsonPropertyName("credentialId")]
    public required string CredentialId { get; init; }

    /// <summary>Array of transport types (e.g., ["usb", "internal"])</summary>
    [JsonPropertyName("transports")]
    public required string[] Transports { get; init; }

    /// <summary>Whether the authenticator supports PRF extension</summary>
    [JsonPropertyName("prfEnabled")]
    public required bool PrfEnabled { get; init; }

    /// <summary>Whether a resident key (passkey) was created</summary>
    [JsonPropertyName("residentKeyCreated")]
    public required bool ResidentKeyCreated { get; init; }

    /// <summary>AAGUID of the authenticator in UUID format (e.g., "08987058-cadc-4b81-b6e1-30de50dcbe96")</summary>
    [JsonPropertyName("aaguid")]
    public required string Aaguid { get; init; }
}

/// <summary>
/// Result of WebAuthn credential assertion (authentication).
/// </summary>
public record CredentialAssertionResult {
    /// <summary>Base64URL-encoded credential ID</summary>
    [JsonPropertyName("credentialId")]
    public required string CredentialId { get; init; }

    /// <summary>Base64-encoded PRF output (32 bytes), or null if PRF not supported</summary>
    [JsonPropertyName("prfOutputBase64")]
    public string? PrfOutputBase64 { get; init; }
}

/// <summary>
/// Options for WebAuthn credential creation, serialized to JSON for TypeScript.
/// </summary>
public record CreateCredentialOptions {
    /// <summary>Existing credential IDs to exclude (Base64URL)</summary>
    [JsonPropertyName("excludeCredentialIds")]
    public required List<string> ExcludeCredentialIds { get; init; }

    /// <summary>Resident key requirement: "required" | "preferred" | "discouraged"</summary>
    [JsonPropertyName("residentKey")]
    public required string ResidentKey { get; init; }

    /// <summary>Authenticator attachment: "platform" | "cross-platform" or null</summary>
    [JsonPropertyName("authenticatorAttachment")]
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>User verification: "required" | "preferred" | "discouraged"</summary>
    [JsonPropertyName("userVerification")]
    public required string UserVerification { get; init; }

    /// <summary>Attestation: "none" | "indirect" | "direct" | "enterprise"</summary>
    [JsonPropertyName("attestation")]
    public required string Attestation { get; init; }

    /// <summary>Hints for authenticator selection</summary>
    [JsonPropertyName("hints")]
    public required List<string> Hints { get; init; }

    /// <summary>User ID bytes as Base64</summary>
    [JsonPropertyName("userIdBase64")]
    public required string UserIdBase64 { get; init; }

    /// <summary>User display name</summary>
    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    /// <summary>PRF salt as Base64 (derived from profile identifier)</summary>
    [JsonPropertyName("prfSaltBase64")]
    public required string PrfSaltBase64 { get; init; }
}

/// <summary>
/// Options for WebAuthn credential assertion, serialized to JSON for TypeScript.
/// </summary>
public record GetCredentialOptions {
    /// <summary>Allowed credential IDs (Base64URL)</summary>
    [JsonPropertyName("allowCredentialIds")]
    public required List<string> AllowCredentialIds { get; init; }

    /// <summary>Known transports for each credential (parallel array)</summary>
    [JsonPropertyName("transportsPerCredential")]
    public required List<string[]> TransportsPerCredential { get; init; }

    /// <summary>User verification: "required" | "preferred" | "discouraged"</summary>
    [JsonPropertyName("userVerification")]
    public required string UserVerification { get; init; }

    /// <summary>PRF salt as Base64 (derived from profile identifier)</summary>
    [JsonPropertyName("prfSaltBase64")]
    public required string PrfSaltBase64 { get; init; }
}

/// <summary>
/// Binding for navigator.credentials WebAuthn API via minimal TypeScript shim.
/// Provides strongly-typed C# API for WebAuthn credential creation and assertion.
/// </summary>
public interface INavigatorCredentialsBinding {
    /// <summary>
    /// Creates a new WebAuthn credential with PRF extension.
    /// </summary>
    Task<Result<CredentialCreationResult>> CreateCredentialAsync(
        CreateCredentialOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an assertion from a WebAuthn credential with PRF extension.
    /// </summary>
    Task<Result<CredentialAssertionResult>> GetCredentialAsync(
        GetCredentialOptions options,
        CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("browser")]
public class NavigatorCredentialsBinding : INavigatorCredentialsBinding {
    private readonly IJsModuleLoader _moduleLoader;
    private readonly ILogger<NavigatorCredentialsBinding> _logger;

    public NavigatorCredentialsBinding(
        IJsModuleLoader moduleLoader,
        ILogger<NavigatorCredentialsBinding> logger) {
        _moduleLoader = moduleLoader;
        _logger = logger;
    }

    private IJSObjectReference Module => _moduleLoader.GetModule("navigatorCredentialsShim");

    public async Task<Result<CredentialCreationResult>> CreateCredentialAsync(
        CreateCredentialOptions options,
        CancellationToken cancellationToken = default) {
        try {
            var optionsJson = JsonSerializer.Serialize(options, JsonOptions.CamelCaseOmitNull);
            _logger.LogDebug(nameof(CreateCredentialAsync) + ": Creating WebAuthn credential with options: {Options}", optionsJson);

            var result = await Module.InvokeAsync<CredentialCreationResult>(
                "createCredential",
                cancellationToken,
                optionsJson);

            if (!result.PrfEnabled) {
                _logger.LogWarning(nameof(CreateCredentialAsync) + ": Authenticator does not support PRF extension");
                return Result.Fail<CredentialCreationResult>(
                    "This authenticator (or possibly the OS) does not support the required WebAuthn PRF extension.");
            }

            if (!result.ResidentKeyCreated) {
                _logger.LogWarning(nameof(CreateCredentialAsync) + ": Authenticator did not create a resident key");
                return Result.Fail<CredentialCreationResult>(
                    "This authenticator does not support resident keys (passkeys).");
            }

            _logger.LogInformation(
                nameof(CreateCredentialAsync) + ": WebAuthn credential created - CredentialId: {CredentialId}, Transports: [{Transports}], " +
                "AuthenticatorAttachment requested: {AuthenticatorAttachment}",
                result.CredentialId,
                string.Join(", ", result.Transports),
                options.AuthenticatorAttachment ?? "(none/null)");
            return Result.Ok(result);
        }
        catch (JSException jsEx) {
            _logger.LogError(jsEx, nameof(CreateCredentialAsync) + ": JavaScript error during WebAuthn credential creation");
            return Result.Fail<CredentialCreationResult>(
                new Error("WebAuthn credential creation failed")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "createCredential"));
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(CreateCredentialAsync) + ": Unexpected error during WebAuthn credential creation");
            return Result.Fail<CredentialCreationResult>(
                new Error("Unexpected error during WebAuthn credential creation")
                    .CausedBy(ex));
        }
    }

    public async Task<Result<CredentialAssertionResult>> GetCredentialAsync(
        GetCredentialOptions options,
        CancellationToken cancellationToken = default) {
        try {
            var optionsJson = JsonSerializer.Serialize(options, JsonOptions.CamelCaseOmitNull);
            _logger.LogDebug(nameof(GetCredentialAsync) + ": Getting WebAuthn assertion with options: {Options}", optionsJson);

            var result = await Module.InvokeAsync<CredentialAssertionResult>(
                "getCredential",
                cancellationToken,
                optionsJson);

            if (result.PrfOutputBase64 is null) {
                _logger.LogWarning(nameof(GetCredentialAsync) + ": Authenticator did not return PRF output");
                return Result.Fail<CredentialAssertionResult>(
                    "This authenticator did not return PRF results. It may not support the PRF extension.");
            }

            _logger.LogInformation(
                nameof(GetCredentialAsync) + ": WebAuthn assertion successful - CredentialId: {CredentialId}, PRF output length: {PrfLength} bytes",
                result.CredentialId,
                result.PrfOutputBase64 is not null ? Convert.FromBase64String(result.PrfOutputBase64).Length : 0);
            return Result.Ok(result);
        }
        catch (JSException jsEx) {
            _logger.LogError(jsEx, nameof(GetCredentialAsync) + ": JavaScript error during WebAuthn assertion");
            return Result.Fail<CredentialAssertionResult>(
                new Error("WebAuthn assertion failed")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "getCredential"));
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(GetCredentialAsync) + ": Unexpected error during WebAuthn assertion");
            return Result.Fail<CredentialAssertionResult>(
                new Error("Unexpected error during WebAuthn assertion")
                    .CausedBy(ex));
        }
    }
}
