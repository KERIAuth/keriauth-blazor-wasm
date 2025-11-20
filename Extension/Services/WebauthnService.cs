using FluentResults;
using JsBind.Net;
using Extension.Models;
using Extension.Models.Storage;
using Extension.Services.Storage;
using Microsoft.JSInterop;
using System.Text;
using System.Text.Json;
using WebExtensions.Net;
// using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Extension.Services {
    public class WebauthnService(IJSRuntime jsRuntime, IJsRuntimeAdapter jsRuntimeAdapter, IStorageService storageService, ILogger<WebauthnService> logger) : IWebauthnService {
        private readonly IStorageService _storageService = storageService;
        private IJSObjectReference? interopModule;
        private WebExtensionsApi? webExtensionsApi;

        private async Task<Result> Initialize() {
            // TODO P2 might be able to instead move some of this into program.cs and inject these as parameters
            try {
                webExtensionsApi ??= new WebExtensionsApi(jsRuntimeAdapter);
                interopModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "/scripts/es6/webauthnCredentialWithPRF.js");
                return Result.Ok();
            }
            catch (JSException jsEx) {
                logger.LogError("Could not initialize {e}", jsEx.Message);
                return Result.Fail(new JavaScriptInteropError("WebAuthn module import", jsEx.Message, jsEx));
            }
            catch (Exception ex) {
                logger.LogError("Could not initialize {e}", ex.Message);
                return Result.Fail(new JavaScriptInteropError("WebAuthn initialization", ex.Message, ex));
            }
        }

        public async Task<Result<CredentialWithPRF>> RegisterCredentialAsync(List<string> registeredCredIds, string residentKey, string authenticatorAttachment, string userVerification, string attestationConveyancePreference, List<string> hints) {
            try {
                // Attempt to call the JavaScript function and map to the CredentialWithPRF type
                var initResult = await Initialize();
                if (initResult.IsFailed) {
                    return Result.Fail<CredentialWithPRF>(initResult.Errors);
                }
                var credential = await interopModule!.InvokeAsync<CredentialWithPRF>("registerCredential", registeredCredIds, residentKey, authenticatorAttachment, userVerification, attestationConveyancePreference, hints);
                // logger.LogWarning("credential: {c}", credential);
                return Result.Ok(credential);
            }
            catch (JSException jsEx) {
                // Return a failure with a meaningful message and error metadata
                return Result.Fail(new FluentResults.Error("JavaScript error occurred")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "registerCredential"));
            }
            catch (Exception ex) {
                // Return a failure for unexpected exceptions
                return Result.Fail(new FluentResults.Error("Unexpected error occurred")
                    .CausedBy(ex));
            }
        }

        private static readonly JsonSerializerOptions jsonSerializerOptions = new() {
            PropertyNameCaseInsensitive = false,
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
            //Converters =
            //{
            //    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            //}
        };

        public async Task<Result<AuthenticateCredResult>> AuthenticateCredential(List<string> credentialIdBase64) {
            // logger.LogWarning("credentialIdBase64s: {r}", credentialIdBase64s);
            try {
                // Attempt to authenticate the credential and re-compute the encryption key
                var initResult = await Initialize();
                if (initResult.IsFailed) {
                    return Result.Fail<AuthenticateCredResult>(initResult.Errors);
                }
                var authenticateCredResult = await interopModule!.InvokeAsync<AuthenticateCredResult>("authenticateCredential", credentialIdBase64);

                if (authenticateCredResult is not null) {
                    return Result.Ok(authenticateCredResult);
                }
                else {
                    return Result.Fail("failed to authenticate credential");
                }
            }
            catch (JSException jsEx) {
                // Return a failure with a meaningful message and error metadata
                return Result.Fail(new FluentResults.Error("JavaScript error occurred")
                    .CausedBy(jsEx)
                    .WithMetadata("Function", "authenticateCredential"));
            }
            catch (Exception ex) {
                // Return a failure for unexpected exceptions
                return Result.Fail(new FluentResults.Error("Unexpected error occurred in AuthenticateCredential")
                    .CausedBy(ex));
            }
        }

        private static string GetEncryptKeyBase64(string encryptKey) {
            // logger.LogWarning("encryptKey original length (chars): {b}", encryptKey.Length);

            // Step 1: Adjust the raw key to ensure 32 credentialIdBytes (256 bits)
            byte[] rawKeyBytes = Encoding.UTF8.GetBytes(encryptKey);

            // Truncate or pad the key to exactly 32 credentialIdBytes
            byte[] adjustedKeyBytes = new byte[32];
            Buffer.BlockCopy(rawKeyBytes, 0, adjustedKeyBytes, 0, Math.Min(rawKeyBytes.Length, 32));

            // Step 2: Convert the adjusted raw key to Base64
            string encryptKeyBase64 = Convert.ToBase64String(adjustedKeyBytes);
            // logger.LogWarning("encryptKeyBase64 adjusted length (chars): {b}", encryptKeyBase64.Length);

            return encryptKeyBase64;
        }

        public async Task<Result<string>> RegisterAttestStoreAuthenticator(string residentKey, string authenticatorAttachment, string userVerification, string attestationConveyancePreference, List<string> hints) {
            await Initialize();
            // Get list of currently registered authenticators, so there isn't an attempt to create redundant credentials (i.e., same RP and user) on same authenticator
            var registeredAuthenticators = await GetRegisteredAuthenticators() ?? throw new InvalidOperationException("RegisteredAuthenticators list is null.");

            // populate a list of registered authenticator credential ids
            List<string> registeredCredIds = [];
            foreach (var authenticator in registeredAuthenticators.Authenticators) {
                registeredCredIds.Add(authenticator.CredentialBase64);
            }

            // Register a new authenticator, excluding reregistering any authenticator already having a credential with same RP and user
            var credentialRet = await RegisterCredentialAsync(registeredCredIds, residentKey, authenticatorAttachment, userVerification, attestationConveyancePreference, hints);
            if (credentialRet is null || credentialRet.IsFailed) {
                return Result.Fail("Failed to register authenticator 333");
            }

            await jsRuntime.InvokeVoidAsync("alert", "Step 1 of 2 registering authenticator successful. Now, we'll confirm this authenticator and OS are sufficiently capable.");

            // Now that authenticator is registered, we need to confirm that with user, and get the encrypt key that is derrived from the PRF attestation
            // First, some prep
            CredentialWithPRF credential = credentialRet.Value;
            string credentialIdBase64Url = credential.CredentialId;
            // logger.LogWarning("credentialIdBase64: {b}", credentialIdBase64Url);
            List<string> credentialIds = [];
            credentialIds.Add(credentialIdBase64Url);

            // Get the attestation from same authenticator
            var encryptKeyBase64Ret = await AuthenticateCredential(credentialIds);
            if (encryptKeyBase64Ret is null || encryptKeyBase64Ret.IsFailed) {
                return Result.Fail("Failed to verify with authenticator 444");
            }

            // Get the cleartext passcode from session storage using new type-safe model
            var passcodeResult = await _storageService.GetItem<PasscodeModel>(StorageArea.Session);
            if (passcodeResult.IsSuccess && passcodeResult.Value != null) {
                try {
                    var passcode = passcodeResult.Value.Passcode;
                    if (passcode is null) {
                        return Result.Fail("no passcode is cached");
                    }

                    // Encrypt the passcode with the encryptKey generated with the help of PRF from the authenticator
                    // First, convert the encrypt key to Base64
                    var encryptKeyBase64 = GetEncryptKeyBase64(encryptKeyBase64Ret.Value.EncryptKey);

                    // Convert the passcode to Base64
                    byte[] plainBytes = Encoding.UTF8.GetBytes(passcode);
                    string passcodeBase64 = Convert.ToBase64String(plainBytes);

                    // confirm the encryptKey size
                    var encryptKeyBytes = Convert.FromBase64String(encryptKeyBase64);
                    // Console.WriteLine($"Key length in bytes: {encryptKeyBytes.Length}");
                    if (encryptKeyBytes.Length != 16 && encryptKeyBytes.Length != 32) {
                        throw new InvalidOperationException("Encryption key must be 16 or 32 bytes.");
                    }

                    // Encrypt
                    var encryptedPasscodeBase64 = await interopModule!.InvokeAsync<string>("encryptWithNounce", encryptKeyBase64, passcodeBase64);

                    // convert encrypted passcode to bytes
                    var encryptedBytes = Convert.FromBase64String(encryptedPasscodeBase64);

                    // Verify the expected length
                    // Console.WriteLine($"Encrypted data length: {encryptedBytes.Length}");
                    if (encryptedBytes.Length == 0) {
                        throw new InvalidOperationException("Encrypted data is empty or invalid.");
                    }

                    // TODO P2 remove this temporary test to compare encrypt and decrypt
                    var decryptedPasscode = await interopModule!.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, encryptedPasscodeBase64);
                    byte[] dataBytes = Convert.FromBase64String(decryptedPasscode);
                    string plainTextPasscode = Encoding.UTF8.GetString(dataBytes);
                    // logger.LogWarning("decryptedPasscode: {p} passcode {pp}", plainTextPasscode, passcode);
                    if (plainTextPasscode != passcode) {
                        // logger.LogError("passcode failed to encrypt-decrypt");
                        return Result.Fail("passcode failed to encrypt-decrypt");
                    }

                    // Append the new registered authenticator and prepare set for storage
                    var creationTime = DateTime.UtcNow;
                    var newRA = new Models.RegisteredAuthenticator() {
                        CreationTime = creationTime,
                        LastUpdatedUtc = creationTime,
                        CredentialBase64 = credentialRet.Value.CredentialId,
                        EncryptedPasscodeBase64 = encryptedPasscodeBase64,
                        Name = $"Unnamed Authenticator"
                    };
                    registeredAuthenticators.Authenticators.Add(newRA);

                    // store updated registeredAuthenticators using storage service
                    var storeResult = await _storageService.SetItem(registeredAuthenticators, StorageArea.Sync);
                    if (storeResult.IsFailed) {
                        logger.LogError("Failed to store RegisteredAuthenticators: {Errors}",
                            string.Join(", ", storeResult.Errors));
                        return Result.Fail("Failed to store authenticator registration");
                    }

                    // return the name of the newly added authenticatorRegistration
                    return Result.Ok(newRA.Name);
                }
                catch (JSException jsEx) {
                    throw new ArgumentException(jsEx.Message);
                }
                catch (Exception ex) {
                    logger.LogError("{m}", ex.Message);
                    return Result.Fail(ex.ToString());
                }
            }
            else {
                return Result.Fail("failed to retreive passcode from cache");
            }
        }

        // TODO P2 migrate to Result pattern
        public async Task<RegisteredAuthenticators> GetRegisteredAuthenticators() {
            await Initialize();

            // Get registered authenticators from sync storage using storage service
            var result = await _storageService.GetItem<RegisteredAuthenticators>(StorageArea.Sync);

            if (result.IsSuccess && result.Value != null) {
                return result.Value;
            }

            // Return empty list if not found
            return new RegisteredAuthenticators();
        }

        public async Task<Result<string>> AuthenticateAKnownCredential() {
            await Initialize();
            RegisteredAuthenticators ras = await GetRegisteredAuthenticators();

            if (ras is null || ras.Authenticators.Count == 0) {
                logger.LogWarning("no registered authenticators");
                return Result.Fail("no registered authenticators");
            }

            List<string> registeredCredIds = [];
            foreach (var registeredAuthenticator in ras.Authenticators) {
                registeredCredIds.Add(registeredAuthenticator.CredentialBase64);
            }

            var authenticateCredResult = await AuthenticateCredential(registeredCredIds);

            if (authenticateCredResult is null || authenticateCredResult.IsFailed) {
                logger.LogWarning("Failed to get result from authenticator");
                return Result.Fail("Failed to get result from authenticator");
            }
            // logger.LogWarning("success value: {s}", authenticateCredResult.Value);

            // Find the registered authenticator matching the credentialID, decrypt its encrypted passcode, and return that
            foreach (var registeredCred in ras.Authenticators) {
                if (registeredCred.CredentialBase64 == authenticateCredResult.Value.CredentialId) {
                    try {
                        string encryptKeyBase64 = GetEncryptKeyBase64(authenticateCredResult.Value.EncryptKey);
                        var decryptedPasscode = await interopModule!.InvokeAsync<string>("decryptWithNounce", encryptKeyBase64, registeredCred.EncryptedPasscodeBase64);
                        // logger.LogWarning("decryptedPasscode {p}", decryptedPasscode);
                        byte[] decryptedPasscodeBytes = Convert.FromBase64String(decryptedPasscode);
                        string decryptedPlaintextPasscode = Encoding.UTF8.GetString(decryptedPasscodeBytes);
                        // logger.LogWarning("plainText {p}", decryptedPlaintextPasscode);
                        return Result.Ok(decryptedPlaintextPasscode);
                    }
                    catch {
                        return Result.Fail("Could not decrypt");
                    }
                }
            }
            return Result.Fail($"Could not decrypt passcode based on webauthn credential");
        }
    }
}
